using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using zxadocslib.Dtos;
using zxavoxpaymentbe.Entities;
using zxavoxpaymentbe.Services;

namespace zxavoxpaymentapi.Tests;

/// <summary>ZD-201: until a biller is integrated, a utility payment fails clearly so Zxadocs releases the funds.</summary>
[Collection(ApiCollection.Name)]
public class UtilityPaymentTests(ApiFixture fixture)
{
    [Fact]
    public async Task A_utility_payment_fails_as_not_configured_and_the_failure_is_published()
    {
        var org = Random.Shared.Next(4_000_000, 5_000_000);
        var created = await fixture.Client().PostAsJsonAsync("/provider-accounts", new ProviderAccountInput(
            org, "UTILITY", "https://biller.example", "UG", "UGX", "DEPOSIT", "", "B2C", false, true, null));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var reference = "PC-" + Guid.NewGuid().ToString("N")[..8];
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<zxavoxpaymentbe.PaymentsDbContext>();
            var clientId = await db.ApiClients.Where(c => c.Name == "zxadocs").Select(c => c.Id).SingleAsync();
            await scope.ServiceProvider.GetRequiredService<PaymentProcessor>().Accept(clientId, new PaymentRequested(Guid.NewGuid(), "corr", org,
                reference, "UTILITY", "DEPOSIT", 80_000m, "UGX", new PaymentPayee("ACCOUNT", "04123456789", "Head office", "UMEME"), "Bill", "May"), default);
            await db.SaveChangesAsync();
        }

        for (var i = 0; i < 100; i++)
        {
            await using var db = fixture.NewDb();
            var order = await db.PaymentOrders.AsNoTracking().SingleAsync(o => o.ExternalRef == reference);
            if (order.Status == PaymentOrderStatus.Failed)
            {
                Assert.Equal("UTILITY_NOT_CONFIGURED", order.ReasonCode);
                Assert.Equal("payment.failed.utility", (await db.OutboxMessages.SingleAsync(m => m.TransactionId == reference)).RoutingKey);
                return;
            }
            await Task.Delay(200);
        }
        Assert.Fail("The utility payment did not fail");
    }
}
