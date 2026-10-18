using Microsoft.EntityFrameworkCore;
using zxavoxpaymentbe.Entities;

namespace zxavoxpaymentbe.Messaging;

/// <summary>Applies an event at most once per consumer (ZD-319).</summary>
public interface IInbox
{
    /// <summary>
    /// Runs work and records the event in one transaction, unless this consumer already applied it.
    /// Work stages its changes on the same context. Returns false for a duplicate.
    /// </summary>
    Task<bool> ProcessOnce(Guid eventId, string consumer, Func<Task> work, CancellationToken ct);
}

public class Inbox(PaymentsDbContext db) : IInbox
{
    public async Task<bool> ProcessOnce(Guid eventId, string consumer, Func<Task> work, CancellationToken ct)
    {
        if (await db.ProcessedMessages.AnyAsync(m => m.EventId == eventId && m.Consumer == consumer, ct))
            return false;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await work();
        db.ProcessedMessages.Add(new ProcessedMessage { EventId = eventId, Consumer = consumer, ProcessedAt = DateTime.UtcNow });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            // Another delivery of the same event won the race: its changes stand and these are dropped.
            if (await db.ProcessedMessages.AnyAsync(m => m.EventId == eventId && m.Consumer == consumer, ct))
                return false;
            throw;
        }
        await transaction.CommitAsync(ct);
        return true;
    }
}
