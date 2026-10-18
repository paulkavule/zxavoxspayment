using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace zxavoxpaymentbe.Entities;

/// <summary>A system allowed to call this API (ZD-338), e.g. Zxadocs. Only the SHA-256 of its key is stored.</summary>
public class ApiClient
{
    public int Id { get; set; }

    [Column(TypeName = "varchar(60)")]
    public string Name { get; set; } = string.Empty;

    [Column(TypeName = "varchar(64)")]
    public string KeyHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Credentials for one provider (ZD-338): the platform's (OrganisationId null) or an organisation's own,
/// which takes precedence. Secrets are one AES-GCM encrypted JSON object and never returned.
/// </summary>
public class ProviderAccount
{
    public int Id { get; set; }

    /// <summary>Null for the platform account that serves every organisation without its own.</summary>
    public int? OrganisationId { get; set; }

    /// <summary>MTN, AIRTEL or UTILITY.</summary>
    [Column(TypeName = "varchar(20)")]
    public string Provider { get; set; } = string.Empty;

    [Column(TypeName = "varchar(200)")]
    public string BaseUrl { get; set; } = string.Empty;

    [Column(TypeName = "varchar(2)")]
    public string Country { get; set; } = "UG";

    [Column(TypeName = "varchar(3)")]
    public string Currency { get; set; } = "UGX";

    /// <summary>MTN: DEPOSIT (default) or TRANSFER.</summary>
    [Column(TypeName = "varchar(10)")]
    public string DefaultMethod { get; set; } = "DEPOSIT";

    /// <summary>MTN X-Target-Environment, e.g. sandbox or mtnuganda.</summary>
    [Column(TypeName = "varchar(40)")]
    public string TargetEnvironment { get; set; } = string.Empty;

    /// <summary>Airtel transaction type: B2C (default) or B2B.</summary>
    [Column(TypeName = "varchar(10)")]
    public string TransactionType { get; set; } = "B2C";

    public bool MessageSigningEnabled { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Encrypted JSON of the provider's secrets (keys, PIN, callback HMAC key).</summary>
    public string Secrets { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum PaymentOrderStatus
{
    Received = 1,
    Submitted = 2,
    Pending = 3,

    /// <summary>The submission's outcome is not known (timeout, 5xx); only a status enquiry may resolve it.</summary>
    PendingUnknown = 4,
    Successful = 5,
    Failed = 6,
}

/// <summary>
/// One payment at a provider, created from a payment.requested event. Unique per client and external
/// reference, so a redelivered event never makes a second provider call; ProviderReferenceId is
/// generated and saved before the call and is the key for every status enquiry.
/// </summary>
public class PaymentOrder
{
    public long Id { get; set; }
    public int ClientId { get; set; }
    public int OrganisationId { get; set; }

    /// <summary>The Zxadocs request reference.</summary>
    [Column(TypeName = "varchar(60)")]
    public string ExternalRef { get; set; } = string.Empty;

    public Guid EventId { get; set; }

    [Column(TypeName = "varchar(64)")]
    public string CorrelationId { get; set; } = string.Empty;

    [Column(TypeName = "varchar(20)")]
    public string Provider { get; set; } = string.Empty;

    public int? ProviderAccountId { get; set; }

    [Column(TypeName = "varchar(10)")]
    public string Method { get; set; } = "DEPOSIT";

    [Column(TypeName = "numeric(18,2)")]
    public decimal Amount { get; set; }

    [Column(TypeName = "varchar(3)")]
    public string Currency { get; set; } = "UGX";

    [Column(TypeName = "varchar(10)")]
    public string PayeeType { get; set; } = string.Empty;

    [Column(TypeName = "varchar(40)")]
    public string PayeeId { get; set; } = string.Empty;

    [Column(TypeName = "varchar(100)")]
    public string PayeeName { get; set; } = string.Empty;

    [Column(TypeName = "varchar(40)")]
    public string Biller { get; set; } = string.Empty;

    [Column(TypeName = "varchar(160)")]
    public string PayerMessage { get; set; } = string.Empty;

    [Column(TypeName = "varchar(160)")]
    public string PayeeNote { get; set; } = string.Empty;

    public PaymentOrderStatus Status { get; set; } = PaymentOrderStatus.Received;
    public Guid ProviderReferenceId { get; set; }

    [Column(TypeName = "varchar(100)")]
    public string FinancialTransactionId { get; set; } = string.Empty;

    /// <summary>Receipt or token returned by a utility biller (ZD-201).</summary>
    [Column(TypeName = "varchar(200)")]
    public string Receipt { get; set; } = string.Empty;

    [Column(TypeName = "varchar(60)")]
    public string ReasonCode { get; set; } = string.Empty;

    [Column(TypeName = "varchar(500)")]
    public string ReasonMessage { get; set; } = string.Empty;

    public int StatusChecks { get; set; }
    public DateTime? NextStatusCheckAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Postgres xmin, so the consumer, poller and callbacks cannot overwrite each other.</summary>
    [Timestamp]
    public uint Version { get; set; }
}

/// <summary>One HTTP call to a provider, with masked request and response, for support and audit.</summary>
public class ProviderCall
{
    public long Id { get; set; }
    public long? PaymentOrderId { get; set; }

    [Column(TypeName = "varchar(20)")]
    public string Provider { get; set; } = string.Empty;

    [Column(TypeName = "varchar(40)")]
    public string Operation { get; set; } = string.Empty;

    public int? HttpStatus { get; set; }
    public int DurationMs { get; set; }
    public string Request { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;

    [Column(TypeName = "varchar(64)")]
    public string CorrelationId { get; set; } = string.Empty;

    public DateTime At { get; set; }
}

/// <summary>A beneficiary name check (ZD-196), with the number masked.</summary>
public class BeneficiaryLookupLog
{
    public long Id { get; set; }
    public int OrganisationId { get; set; }

    [Column(TypeName = "varchar(20)")]
    public string Provider { get; set; } = string.Empty;

    [Column(TypeName = "varchar(20)")]
    public string MaskedMsisdn { get; set; } = string.Empty;

    public bool Active { get; set; }

    [Column(TypeName = "varchar(100)")]
    public string Name { get; set; } = string.Empty;

    [Column(TypeName = "varchar(200)")]
    public string Error { get; set; } = string.Empty;

    public DateTime LookedUpAt { get; set; }
}

/// <summary>A provider callback as received. Only ever a hint to re-query the status, never trusted for the outcome.</summary>
public class CallbackReceipt
{
    public long Id { get; set; }

    [Column(TypeName = "varchar(20)")]
    public string Provider { get; set; } = string.Empty;

    [Column(TypeName = "varchar(100)")]
    public string ReferenceId { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;
    public bool Verified { get; set; }
    public DateTime ReceivedAt { get; set; }
}
