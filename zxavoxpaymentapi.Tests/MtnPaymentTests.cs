using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using zxadocslib.Dtos;
using zxavoxpaymentbe.Entities;
using zxavoxpaymentbe.Messaging;
using zxavoxpaymentbe.Services;

namespace zxavoxpaymentapi.Tests;

/// <summary>ZD-160 / ZD-260: MTN payouts follow the plan 11.6 outcome mapping and are never sent twice.</summary>
[Collection(ApiCollection.Name)]
public class MtnPaymentTests(ApiFixture fixture)
{
    [Fact]
    public async Task A_requested_payment_is_paid_once_and_its_completion_published()
    {
        var (mtn, org) = await Account();
        mtn.Status = () => FakeMtn.Json(200, """{"status":"SUCCESSFUL","financialTransactionId":"FT-777"}""");
        await using var connection = await fixture.Broker.Factory().CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(MessagingNames.PaymentsExchange, ExchangeType.Topic, durable: true);
        var queue = (await channel.QueueDeclareAsync("", durable: false, exclusive: true, autoDelete: true)).QueueName;
        await channel.QueueBindAsync(queue, MessagingNames.PaymentsExchange, "payment.completed.mtn");

        var reference = "PC-" + Guid.NewGuid().ToString("N")[..8];
        await Publish(channel, Requested(org, reference));
        await Publish(channel, Requested(org, reference));
        var order = await WaitFor(reference, o => o.Status == PaymentOrderStatus.Submitted);

        var callback = await fixture.Client(null).PostAsync($"/callbacks/mtn/{order.ProviderReferenceId}", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        order = await WaitFor(reference, o => o.Status == PaymentOrderStatus.Successful);

        Assert.Equal("FT-777", order.FinancialTransactionId);
        Assert.Equal(1, mtn.Count("/v1_0/deposit"));
        Assert.Equal(order.ProviderReferenceId.ToString(), mtn.Calls.Single(c => c.Path == "/v1_0/deposit").ReferenceId);
        await using (var db = fixture.NewDb())
        {
            Assert.Equal(1, await db.PaymentOrders.CountAsync(o => o.ExternalRef == reference));
            var logged = await db.ProviderCalls.FirstAsync(c => c.PaymentOrderId == order.Id && c.Operation == "deposit");
            Assert.DoesNotContain("256772123456", logged.Request);
        }
        var completed = await Receive<PaymentCompleted>(channel, queue, reference);
        Assert.Equal(order.Id, completed.PaymentId);
        Assert.Equal("FT-777", completed.FinancialTransactionId);
    }

    [Fact]
    public async Task A_submission_with_no_answer_is_never_resent_and_is_resolved_by_status_checks()
    {
        var (mtn, org) = await Account();
        mtn.Submit = () => throw new HttpRequestException("connection reset");
        mtn.Status = () => FakeMtn.Json(404, """{"code":"RESOURCE_NOT_FOUND"}""");
        var reference = await Accept(org);
        var order = await WaitFor(reference, o => o.Status == PaymentOrderStatus.PendingUnknown && o.UpdatedAt > o.CreatedAt && o.StatusChecks == 0
            && mtn.Count("/v1_0/deposit") == 1);

        var client = fixture.Client();
        var check = await (await client.PostAsync($"/payments/{order.Id}/status-check", null)).Content.ReadFromJsonAsync<PaymentOrder>();
        Assert.Equal(PaymentOrderStatus.PendingUnknown, check!.Status);

        mtn.Status = () => FakeMtn.Json(200, """{"status":"SUCCESSFUL","financialTransactionId":"FT-1"}""");
        check = await (await client.PostAsync($"/payments/{order.Id}/status-check", null)).Content.ReadFromJsonAsync<PaymentOrder>();
        Assert.Equal(PaymentOrderStatus.Successful, check!.Status);
        Assert.Equal(1, mtn.Count("/v1_0/deposit"));
        Assert.Equal(1, mtn.Count("/token/"));
    }

    [Theory]
    [InlineData("RESOURCE_ALREADY_EXIST", PaymentOrderStatus.Submitted)]
    [InlineData("PAYEE_NOT_FOUND", PaymentOrderStatus.Failed)]
    public async Task A_conflict_is_either_an_earlier_submission_or_a_refusal(string code, PaymentOrderStatus expected)
    {
        var (mtn, org) = await Account();
        mtn.Submit = () => FakeMtn.Json(409, $$"""{"code":"{{code}}","message":"m"}""");
        var reference = await Accept(org);
        var order = await WaitFor(reference, o => o.Status == expected);

        await using var db = fixture.NewDb();
        var failure = await db.OutboxMessages.Where(m => m.TransactionId == reference && m.Type == "payment.failed").ToListAsync();
        Assert.Equal(expected == PaymentOrderStatus.Failed ? 1 : 0, failure.Count);
        if (expected == PaymentOrderStatus.Failed)
            Assert.Equal(code, JsonSerializer.Deserialize<PaymentFailed>(failure[0].Payload)!.ReasonCode);
        Assert.Equal(order.ProviderReferenceId, (await db.PaymentOrders.SingleAsync(o => o.Id == order.Id)).ProviderReferenceId);
    }

    [Fact]
    public async Task Without_a_provider_account_the_payment_fails_at_once_and_nothing_is_sent()
    {
        var reference = await Accept(Random.Shared.Next(2_000_000, 3_000_000), provider: "UTILITY");

        await using var db = fixture.NewDb();
        var order = await db.PaymentOrders.SingleAsync(o => o.ExternalRef == reference);
        Assert.Equal(PaymentOrderStatus.Failed, order.Status);
        Assert.Equal("NO_PROVIDER_ACCOUNT", order.ReasonCode);
        var failed = JsonSerializer.Deserialize<PaymentFailed>(
            (await db.OutboxMessages.SingleAsync(m => m.TransactionId == reference && m.Type == "payment.failed")).Payload)!;
        Assert.Equal(order.Id, failed.PaymentId);
        var alert = await db.OutboxMessages.SingleAsync(m => m.TransactionId == reference && m.Type == "payment.alert");
        Assert.Equal("payment.alert.utility", alert.RoutingKey);
        Assert.Equal(("MISCONFIGURED", "NO_PROVIDER_ACCOUNT"),
            (JsonSerializer.Deserialize<PaymentAlert>(alert.Payload)!.Kind, JsonSerializer.Deserialize<PaymentAlert>(alert.Payload)!.Code));
        Assert.False(await db.ProviderCalls.AnyAsync(c => c.PaymentOrderId == order.Id));
    }

    [Fact]
    public async Task A_lookup_returns_the_holders_name_and_logs_the_number_masked()
    {
        var (mtn, org) = await Account();
        var client = fixture.Client();
        var found = await (await client.PostAsJsonAsync("/beneficiaries/lookup", new { organisationId = org, provider = "MTN", msisdn = "256772123456" }))
            .Content.ReadFromJsonAsync<BeneficiaryLookupResponse>();
        Assert.True(found!.Active);
        Assert.Equal("Jane Akello", found.Name);

        mtn.Active = () => FakeMtn.Json(200, """{"result":false}""");
        var inactive = await (await client.PostAsJsonAsync("/beneficiaries/lookup", new { organisationId = org, provider = "MTN", msisdn = "256772123456" }))
            .Content.ReadFromJsonAsync<BeneficiaryLookupResponse>();
        Assert.False(inactive!.Active);

        await using var db = fixture.NewDb();
        Assert.All(await db.BeneficiaryLookups.Where(l => l.OrganisationId == org).ToListAsync(), l => Assert.Equal("256***56", l.MaskedMsisdn));
    }

    private async Task<(FakeMtn Mtn, int Org)> Account()
    {
        var mtn = fixture.Provider.Mtn();
        var org = Random.Shared.Next(1_000_000, 2_000_000);
        var created = await fixture.Client().PostAsJsonAsync("/provider-accounts", new ProviderAccountInput(org, "MTN", mtn.BaseUrl, "UG", "UGX", "DEPOSIT",
            "sandbox", "B2C", false, true, new() { ["subscriptionKey"] = "s", ["apiUser"] = "u", ["apiKey"] = "k" }));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        return (mtn, org);
    }

    private static PaymentRequested Requested(int org, string reference, string provider = "MTN") => new(
        Guid.NewGuid(), Guid.NewGuid().ToString(), org, reference, provider, "DEPOSIT", 50_000m, "UGX",
        new PaymentPayee("MSISDN", "256772123456", "Jane Akello", ""), $"Petty cash {reference}", "Fuel");

    /// <summary>Creates the order as the consumer would, without going through the broker.</summary>
    private async Task<string> Accept(int org, string provider = "MTN")
    {
        var reference = "PC-" + Guid.NewGuid().ToString("N")[..8];
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<zxavoxpaymentbe.PaymentsDbContext>();
        var clientId = await db.ApiClients.Where(c => c.Name == "zxadocs").Select(c => c.Id).SingleAsync();
        await scope.ServiceProvider.GetRequiredService<PaymentProcessor>().Accept(clientId, Requested(org, reference, provider), default);
        await db.SaveChangesAsync();
        return reference;
    }

    private static async Task Publish(IChannel channel, PaymentRequested request)
    {
        var properties = new BasicProperties { Persistent = true, MessageId = request.EventId.ToString(), Type = "payment.requested", CorrelationId = request.CorrelationId };
        await channel.BasicPublishAsync(MessagingNames.PaymentsExchange, "payment.requested.mtn", false, properties,
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)));
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

    private static async Task<T> Receive<T>(IChannel channel, string queue, string reference)
    {
        for (var i = 0; i < 50; i++)
        {
            var message = await channel.BasicGetAsync(queue, autoAck: true);
            var body = message is null ? null : Encoding.UTF8.GetString(message.Body.Span);
            if (body?.Contains(reference) == true)
                return JsonSerializer.Deserialize<T>(body)!;
            if (message is null)
                await Task.Delay(200);
        }
        throw new TimeoutException($"No event for {reference}");
    }
}
