using System.Net;
using Microsoft.EntityFrameworkCore;
using zxadocslib.Dtos;
using zxavoxpaymentbe.Entities;

namespace zxavoxpaymentapi.Tests;

/// <summary>ZD-139 (payment service): a replay goes back to the consumer's queue with the same event id, once.</summary>
[Collection(ApiCollection.Name)]
public class DeadLetterTests(ApiFixture fixture)
{
    [Fact]
    public async Task A_replay_requeues_the_original_event_once()
    {
        var eventId = Guid.NewGuid();
        int id;
        await using (var db = fixture.NewDb())
        {
            var item = new DeadLetterItem
            {
                OrganisationId = 7, EventId = eventId, Consumer = "payment-requests", Queue = "zxavox.payment-requests." + eventId.ToString("N"),
                Type = "payment.requested", TransactionId = "PC-000009", Payload = "{}", Reason = "boom", DeadLetteredAt = DateTime.UtcNow,
            };
            db.DeadLetterItems.Add(item);
            await db.SaveChangesAsync();
            id = item.Id;
        }

        var client = fixture.Client();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/dead-letters/{id}/replay", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/dead-letters/{id}/replay", null)).StatusCode);

        await using var check = fixture.NewDb();
        var staged = await check.OutboxMessages.SingleAsync(m => m.EventId == eventId);
        Assert.Equal("", staged.Exchange);
        Assert.Equal("zxavox.payment-requests." + eventId.ToString("N"), staged.RoutingKey);
        Assert.Equal(DeadLetterStatus.Replayed, (await check.DeadLetterItems.SingleAsync(d => d.Id == id)).Status);
    }
}
