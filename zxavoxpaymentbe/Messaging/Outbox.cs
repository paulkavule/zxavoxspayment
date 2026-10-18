using System.Text.Json;
using zxavoxpaymentbe.Entities;

namespace zxavoxpaymentbe.Messaging;

/// <summary>Stages events for publishing. It only adds: the caller's SaveChanges commits the event with the change.</summary>
public interface IOutbox
{
    /// <summary>Stages an event; transactionId names the business transaction, e.g. the Zxadocs request reference.</summary>
    OutboxMessage Enqueue(int orgId, string type, string exchange, string routingKey, object payload, string transactionId,
        string? correlationId, Guid? eventId = null);
}

public class Outbox(PaymentsDbContext db) : IOutbox
{
    public OutboxMessage Enqueue(int orgId, string type, string exchange, string routingKey, object payload, string transactionId,
        string? correlationId, Guid? eventId = null)
    {
        var message = new OutboxMessage
        {
            EventId = eventId ?? Guid.NewGuid(), OrganisationId = orgId, Type = type, Exchange = exchange, RoutingKey = routingKey,
            TransactionId = transactionId, Payload = payload as string ?? JsonSerializer.Serialize(payload),
            CorrelationId = correlationId, CreatedAt = DateTime.UtcNow,
        };
        db.OutboxMessages.Add(message);
        return message;
    }
}
