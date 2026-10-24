using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using zxadocslib.Dtos;
using zxavoxpaymentbe.Entities;
using zxavoxpaymentbe.Services;

namespace zxavoxpaymentapi.Tests;

/// <summary>ZD-181: Airtel disbursements follow plan 11.7, with the PIN encrypted and the payment never sent twice.</summary>
[Collection(ApiCollection.Name)]
public class AirtelPaymentTests(ApiFixture fixture)
{
    [Fact]
    public async Task A_disbursement_carries_the_encrypted_pin_and_local_number_and_settles_on_enquiry()
    {
        var (airtel, org) = await Account();
        var reference = await Accept(org);
        var order = await WaitFor(reference, o => o.Status == PaymentOrderStatus.Pending);

        var sent = JsonDocument.Parse(airtel.Calls.Single(c => c.Path == "/standard/v2/disbursements/").Body).RootElement;
        Assert.Equal("1234", airtel.DecryptPin(sent.GetProperty("pin").GetString()!));
        Assert.Equal("752123456", sent.GetProperty("payee").GetProperty("msisdn").GetString());
        Assert.Equal(order.ProviderReferenceId.ToString("N"), sent.GetProperty("transaction").GetProperty("id").GetString());
        Assert.Equal(50000, sent.GetProperty("transaction").GetProperty("amount").GetDecimal());

        airtel.Enquiry = () => FakeAirtel.Transaction(200, "TS", "DP00900001001", "MP250101.1234");
        var callback = await fixture.Client(null).PostAsJsonAsync("/callbacks/airtel",
            new { transaction = new { id = order.ProviderReferenceId.ToString("N"), status_code = "TS" } });
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        order = await WaitFor(reference, o => o.Status == PaymentOrderStatus.Successful);

        Assert.Equal("MP250101.1234", order.FinancialTransactionId);
        Assert.Equal(1, airtel.Count("/standard/v2/disbursements/"));
        Assert.True(airtel.Count($"/standard/v2/disbursements/{order.ProviderReferenceId:N}?transactionType=B2C", "GET") >= 1);
    }

    [Theory]
    [InlineData("TA", "DP00900001000", PaymentOrderStatus.PendingUnknown, "")]
    [InlineData("", "DP00900001017", PaymentOrderStatus.Submitted, "")]
    [InlineData("TF", "DP00900001007", PaymentOrderStatus.Failed, "NOT_ENOUGH_FUNDS")]
    [InlineData("TF", "DP00900001012", PaymentOrderStatus.Failed, "DP00900001012")]
    public async Task Each_answer_maps_to_the_plan_outcome(string transactionStatus, string code, PaymentOrderStatus expected, string reason)
    {
        var (airtel, org) = await Account();
        airtel.Disburse = () => FakeAirtel.Transaction(200, transactionStatus, code);
        var reference = await Accept(org);
        var order = await WaitFor(reference, o => o.Status == expected && airtel.Count("/standard/v2/disbursements/") == 1);

        Assert.Equal(reason, order.ReasonCode);
        Assert.Equal(1, airtel.Count("/standard/v2/disbursements/"));
        await using var db = fixture.NewDb();
        var failure = await db.OutboxMessages.Where(m => m.TransactionId == reference && m.Type == "payment.failed").ToListAsync();
        Assert.Equal(expected == PaymentOrderStatus.Failed ? 1 : 0, failure.Count);
        if (failure.Count == 1)
            Assert.Equal(reason, JsonSerializer.Deserialize<PaymentFailed>(failure[0].Payload)!.ReasonCode);
        var alerts = await db.OutboxMessages.Where(m => m.TransactionId == reference && m.Type == "payment.alert").ToListAsync();
        Assert.Equal(reason == "NOT_ENOUGH_FUNDS" ? ["FLOAT_LOW"] : Array.Empty<string>(),
            alerts.Select(a => JsonSerializer.Deserialize<PaymentAlert>(a.Payload)!.Kind));
    }

    [Fact]
    public async Task A_barred_number_is_not_an_active_beneficiary()
    {
        var (airtel, org) = await Account();
        var client = fixture.Client();
        var lookup = new { organisationId = org, provider = "AIRTEL", msisdn = "256752123456" };
        var found = await (await client.PostAsJsonAsync("/beneficiaries/lookup", lookup)).Content.ReadFromJsonAsync<BeneficiaryLookupResponse>();
        Assert.Equal((true, "John Okello"), (found!.Active, found.Name));
        Assert.Contains(airtel.Calls, c => c.Path == "/standard/v1/users/752123456");

        airtel.User = () => FakeMtn.Json(200, """{"data":{"first_name":"John","last_name":"Okello","is_barred":true}}""");
        var barred = await (await client.PostAsJsonAsync("/beneficiaries/lookup", lookup)).Content.ReadFromJsonAsync<BeneficiaryLookupResponse>();
        Assert.False(barred!.Active);
        Assert.Equal("The number is barred", barred.Error);
    }

    private async Task<(FakeAirtel Airtel, int Org)> Account()
    {
        var airtel = fixture.Provider.Airtel();
        var org = Random.Shared.Next(3_000_000, 4_000_000);
        var created = await fixture.Client().PostAsJsonAsync("/provider-accounts", new ProviderAccountInput(org, "AIRTEL", airtel.BaseUrl, "UG", "UGX",
            "DEPOSIT", "", "B2C", false, true, new() { ["clientId"] = "c", ["clientSecret"] = "s", ["pin"] = "1234" }));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        return (airtel, org);
    }

    private async Task<string> Accept(int org)
    {
        var reference = "PC-" + Guid.NewGuid().ToString("N")[..8];
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<zxavoxpaymentbe.PaymentsDbContext>();
        var clientId = await db.ApiClients.Where(c => c.Name == "zxadocs").Select(c => c.Id).SingleAsync();
        await scope.ServiceProvider.GetRequiredService<PaymentProcessor>().Accept(clientId, new PaymentRequested(Guid.NewGuid(), "corr", org, reference,
            "AIRTEL", "DEPOSIT", 50_000m, "UGX", new PaymentPayee("MSISDN", "256752123456", "John Okello", ""), "Petty cash", "Fuel"), default);
        await db.SaveChangesAsync();
        return reference;
    }

    private async Task<PaymentOrder> WaitFor(string reference, Func<PaymentOrder, bool> done)
    {
        for (var i = 0; i < 100; i++)
        {
            await using var db = fixture.NewDb();
            var order = await db.PaymentOrders.AsNoTracking().FirstOrDefaultAsync(o => o.ExternalRef == reference);
            if (order is not null && done(order))
                return order;
            await Task.Delay(200);
        }
        throw new TimeoutException($"Payment {reference} did not reach the expected state");
    }
}
