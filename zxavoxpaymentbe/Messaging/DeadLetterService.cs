using Microsoft.EntityFrameworkCore;
using zxadocslib.Dtos;
using zxavoxpaymentbe.Entities;

namespace zxavoxpaymentbe.Messaging;

/// <summary>
/// Dead letters of the payment service's queues (ZD-139). A replay goes back through the outbox to the
/// consumer's own queue with the original event id, so the inbox still stops it being applied twice.
/// </summary>
public class DeadLetterService(PaymentsDbContext db, IOutbox outbox)
{
    public async Task<IReadOnlyList<DeadLetterItem>> List(DeadLetterStatus? status) =>
        await db.DeadLetterItems.AsNoTracking().Where(d => status == null || d.Status == status).OrderByDescending(d => d.Id).Take(500).ToListAsync();

    /// <summary>Null when there is no such item; otherwise the item, or why it cannot be replayed.</summary>
    public async Task<(DeadLetterItem? Item, string? Error)> Replay(int clientId, int id)
    {
        var item = await db.DeadLetterItems.FirstOrDefaultAsync(d => d.Id == id);
        if (item is null)
            return (null, null);
        if (item.Status != DeadLetterStatus.Open)
            return (item, "Only an open dead letter can be replayed");
        if (item.EventId is null)
            return (item, "The message has no event id and cannot be replayed safely");

        outbox.Enqueue(item.OrganisationId, item.Type, "", item.Queue, item.Payload, item.TransactionId, item.CorrelationId, item.EventId);
        item.Status = DeadLetterStatus.Replayed;
        item.ActionedBy = clientId;
        item.ActionedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (item, null);
    }
}
