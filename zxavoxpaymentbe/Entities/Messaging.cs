using System.ComponentModel.DataAnnotations.Schema;

namespace zxavoxpaymentbe.Entities;

/// <summary>
/// An event waiting to be published to RabbitMQ (ZD-298). Written in the same transaction as the
/// change that caused it, so a committed change can never lose its event; marked published only
/// after the broker confirms it.
/// </summary>
public class OutboxMessage
{
    public long Id { get; set; }

    /// <summary>The event's identity. A replay re-sends the same EventId so consumers can recognise it.</summary>
    public Guid EventId { get; set; }

    public int OrganisationId { get; set; }

    [Column(TypeName = "varchar(100)")]
    public string Type { get; set; } = string.Empty;

    /// <summary>Empty for the default exchange, where the routing key is a queue name (used by replays).</summary>
    [Column(TypeName = "varchar(100)")]
    public string Exchange { get; set; } = string.Empty;

    [Column(TypeName = "varchar(100)")]
    public string RoutingKey { get; set; } = string.Empty;

    [Column(TypeName = "jsonb")]
    public string Payload { get; set; } = "{}";

    [Column(TypeName = "varchar(64)")]
    public string? CorrelationId { get; set; }

    /// <summary>The business transaction the event belongs to, e.g. a request reference (ZD-319).</summary>
    [Column(TypeName = "varchar(60)")]
    public string TransactionId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }

    [Column(TypeName = "varchar(500)")]
    public string LastError { get; set; } = string.Empty;
}

/// <summary>
/// An event a consumer has already applied (ZD-319). Written in the same transaction as the consumer's
/// changes, so a redelivered event is recognised and acknowledged without applying it twice.
/// </summary>
public class ProcessedMessage
{
    public Guid EventId { get; set; }

    [Column(TypeName = "varchar(100)")]
    public string Consumer { get; set; } = string.Empty;

    public DateTime ProcessedAt { get; set; }
}

/// <summary>
/// A copy of a message moved to a dead-letter queue (ZD-139), kept in the database so operations can
/// inspect it and replay or resolve it without reaching into the broker.
/// </summary>
public class DeadLetterItem
{
    public int Id { get; set; }
    public int OrganisationId { get; set; }

    /// <summary>Null when the message arrived without an event id, which is itself why it was dead-lettered.</summary>
    public Guid? EventId { get; set; }

    [Column(TypeName = "varchar(100)")]
    public string Consumer { get; set; } = string.Empty;

    /// <summary>The queue the consumer reads; a replay is sent straight back to it.</summary>
    [Column(TypeName = "varchar(150)")]
    public string Queue { get; set; } = string.Empty;

    [Column(TypeName = "varchar(100)")]
    public string Type { get; set; } = string.Empty;

    [Column(TypeName = "varchar(60)")]
    public string TransactionId { get; set; } = string.Empty;

    [Column(TypeName = "varchar(64)")]
    public string CorrelationId { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    [Column(TypeName = "varchar(1000)")]
    public string Reason { get; set; } = string.Empty;

    public int Attempts { get; set; }
    public DateTime DeadLetteredAt { get; set; }
    public zxadocslib.Dtos.DeadLetterStatus Status { get; set; } = zxadocslib.Dtos.DeadLetterStatus.Open;
    public int? ActionedBy { get; set; }
    public DateTime? ActionedAt { get; set; }

    [Column(TypeName = "varchar(500)")]
    public string Note { get; set; } = string.Empty;
}
