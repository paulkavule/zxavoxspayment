using zxavoxpaymentbe.Entities;

namespace zxavoxpaymentbe.Providers;

/// <summary>The account a call uses, with its decrypted secrets, and what the call belongs to.</summary>
public record ProviderContext(ProviderAccount Account, IReadOnlyDictionary<string, string> Secrets, string CorrelationId, long? PaymentOrderId);

public enum ProviderState
{
    /// <summary>The provider took the payment; its result comes from a status enquiry.</summary>
    Accepted,
    Pending,
    Successful,
    Failed,

    /// <summary>Timeout, network error or 5xx: the payment may or may not exist. Only a status enquiry may resolve it.</summary>
    Unknown,

    /// <summary>A status enquiry found no such payment.</summary>
    NotFound,

    /// <summary>Nothing reached the provider (e.g. the token call failed), so the submission may be tried again.</summary>
    NotSent,
}

public record ProviderResult(ProviderState State, string ReasonCode = "", string ReasonMessage = "", string FinancialTransactionId = "", string Receipt = "");

public record LookupResult(bool Active, string Name, string Error);

/// <summary>One payment provider (plan 11.6, 11.7). Implementations never resubmit; the processor decides what happens next.</summary>
public interface IPaymentProvider
{
    /// <summary>MTN, AIRTEL or UTILITY, as on ProviderAccount.Provider.</summary>
    string Name { get; }

    Task<ProviderResult> Submit(ProviderContext context, PaymentOrder order, CancellationToken ct);

    Task<ProviderResult> Status(ProviderContext context, PaymentOrder order, CancellationToken ct);

    Task<LookupResult> Lookup(ProviderContext context, string msisdn, CancellationToken ct);

    /// <summary>Fetches a token to prove the credentials work; returns the problem, or null.</summary>
    Task<string?> Test(ProviderContext context, CancellationToken ct);
}
