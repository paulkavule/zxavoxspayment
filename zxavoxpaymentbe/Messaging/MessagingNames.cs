namespace zxavoxpaymentbe.Messaging;

/// <summary>Exchange, header and event names shared by the publisher and the consumers.</summary>
public static class MessagingNames
{
    /// <summary>Durable topic exchange for petty cash payment events, e.g. payment.requested.mtn.</summary>
    public const string PaymentsExchange = "pettycash.payments";

    public const string CorrelationHeader = "x-correlation-id";
    public const string OrganisationHeader = "x-organisation-id";
    public const string TransactionHeader = "x-transaction-id";

    // Retry and dead-letter bookkeeping (ZD-139).
    public const string AttemptHeader = "x-attempt";
    public const string FailureHeader = "x-failure-reason";

    public static string RetryQueue(string queue, int attempt) => $"{queue}.retry.{attempt}";
    public static string DeadLetterQueue(string queue) => $"{queue}.dlq";
}
