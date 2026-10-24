using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using zxavoxpaymentbe.Entities;

namespace zxavoxpaymentbe.Messaging;

/// <summary>
/// Publishes due outbox rows (ZD-298). Rows are claimed with FOR UPDATE SKIP LOCKED, so several workers
/// never take the same row, and a row is marked published only after the broker confirmed it. A crash
/// between confirm and commit re-sends the row: delivery is at least once, so consumers are idempotent.
/// </summary>
public class OutboxDispatcher(PaymentsDbContext db, IMessagePublisher publisher, ILogger<OutboxDispatcher> logger)
{
    /// <summary>Publishes up to batchSize due rows and returns how many were confirmed.</summary>
    public async Task<int> PublishDueAsync(int batchSize, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var due = await db.OutboxMessages.FromSqlRaw("""
            SELECT * FROM payments_api."OutboxMessages"
            WHERE "PublishedAt" IS NULL AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= now())
            ORDER BY "Id" LIMIT {0} FOR UPDATE SKIP LOCKED
            """, batchSize).ToListAsync(ct);

        var published = 0;
        foreach (var message in due)
        {
            try
            {
                await publisher.PublishAsync(message, ct);
                message.PublishedAt = DateTime.UtcNow;
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                // Back off 5s, 10s, 20s ... capped at 5 minutes; the rest of the batch waits for the next pass.
                message.NextAttemptAt = DateTime.UtcNow.AddSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(message.Attempts - 1, 6))));
                logger.LogWarning(ex, "Outbox event {EventId} ({Type}) was not published, attempt {Attempts}",
                    message.EventId, message.Type, message.Attempts);
                break;
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return published;
    }
}
