using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using zxadocslib.Dtos;
using zxavoxpaymentbe.Entities;
using zxavoxpaymentbe.Messaging;
using zxavoxpaymentbe.Providers;

namespace zxavoxpaymentbe.Services;

/// <summary>
/// The payment order lifecycle (plan 11.3, 11.6). An event creates the order with its provider reference
/// already saved; submission marks it PendingUnknown and commits BEFORE the provider call, so a crash
/// mid-call leaves it to the status poller and it is never sent twice. Final outcomes stage
/// payment.completed or payment.failed in the outbox with the same save.
/// </summary>
public class PaymentProcessor(
    PaymentsDbContext db, ProviderAccountService accounts, IEnumerable<IPaymentProvider> providers, IOutbox outbox, ILogger<PaymentProcessor> logger)
{
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1)];
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private const int MaxSubmitAttempts = 5;

    /// <summary>Failures caused by our own credentials or setup, not by the payee (plan 11.7).</summary>
    private static readonly string[] ConfigurationCodes =
        ["PROVIDER_AUTH_FAILED", "DP00900001016", "ROUTER001", "ROUTER005", "ROUTER007", "ROUTER114", "ROUTER115", "ROUTER116"];

    /// <summary>How long a payment is polled before it is left for reconciliation (PAY_STATUS_CUTOFF_HOURS, default 24).</summary>
    public static TimeSpan Cutoff { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Stages the order for a payment.requested event; a second event for the same reference is ignored.</summary>
    public async Task Accept(int clientId, PaymentRequested request, CancellationToken ct)
    {
        if (await db.PaymentOrders.AnyAsync(o => o.ClientId == clientId && o.ExternalRef == request.ExternalRef, ct))
        {
            logger.LogWarning("Payment {ExternalRef} already exists; event {EventId} ignored", request.ExternalRef, request.EventId);
            return;
        }

        var account = await accounts.Resolve(request.OrganisationId, request.Provider);
        var now = DateTime.UtcNow;
        var order = new PaymentOrder
        {
            ClientId = clientId, OrganisationId = request.OrganisationId, ExternalRef = request.ExternalRef, EventId = request.EventId,
            CorrelationId = request.CorrelationId, Provider = request.Provider, ProviderAccountId = account?.Id,
            Method = account?.DefaultMethod ?? request.Method, Amount = request.Amount, Currency = request.Currency,
            PayeeType = request.Payee.IdType, PayeeId = request.Payee.Id, PayeeName = request.Payee.Name, Biller = request.Payee.Biller,
            PayerMessage = request.PayerMessage, PayeeNote = request.PayeeNote, ProviderReferenceId = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now,
        };
        db.PaymentOrders.Add(order);
        if (account is null)
        {
            // Saved first (inside the inbox transaction) so the failure event carries the payment id.
            await db.SaveChangesAsync(ct);
            Finish(order, PaymentOrderStatus.Failed, "NO_PROVIDER_ACCOUNT", $"No active {request.Provider} account for the organisation or the platform");
        }
    }

    /// <summary>Submits one waiting order. False when none is waiting.</summary>
    public async Task<bool> SubmitNext(CancellationToken ct)
    {
        var order = await Claim(PaymentOrderStatus.Received, ct, o => o.Status = PaymentOrderStatus.PendingUnknown);
        if (order is null)
            return false;
        var result = await Provider(order).Submit(await Context(order), order, ct);
        await Apply(order, result, submitting: true, ct);
        return true;
    }

    /// <summary>Asks the provider about one order whose status check is due. False when none is due.</summary>
    public async Task<bool> CheckNextDue(CancellationToken ct)
    {
        var order = await Claim(null, ct, o => o.StatusChecks++);
        if (order is null)
            return false;
        await Apply(order, await Provider(order).Status(await Context(order), order, ct), submitting: false, ct);
        return true;
    }

    /// <summary>A status enquiry on demand (POST /payments/{id}/status-check). Never resubmits.</summary>
    public async Task<PaymentOrder?> CheckNow(int clientId, long id, CancellationToken ct)
    {
        var order = await db.PaymentOrders.FirstOrDefaultAsync(o => o.Id == id && o.ClientId == clientId, ct);
        if (order is null || order.Status is PaymentOrderStatus.Received or PaymentOrderStatus.Successful or PaymentOrderStatus.Failed)
            return order;
        order.StatusChecks++;
        await Apply(order, await Provider(order).Status(await Context(order), order, ct), submitting: false, ct);
        return order;
    }

    /// <summary>Records a provider callback and brings the order's next status check forward. The body is never trusted.</summary>
    public async Task Hint(string provider, string referenceId, string body, bool verified, CancellationToken ct)
    {
        var reference = Guid.TryParse(referenceId, out var parsed) ? parsed : Guid.Empty;
        db.CallbackReceipts.Add(new CallbackReceipt
        {
            Provider = provider, ReferenceId = referenceId.Length > 100 ? referenceId[..100] : referenceId,
            Body = ProviderHttp.Mask(body.Length > 4000 ? body[..4000] : body), Verified = verified, ReceivedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        await db.PaymentOrders
            .Where(o => o.Provider == provider && o.ProviderReferenceId == reference
                && (o.Status == PaymentOrderStatus.Submitted || o.Status == PaymentOrderStatus.Pending || o.Status == PaymentOrderStatus.PendingUnknown))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.NextStatusCheckAt, DateTime.UtcNow), ct);
    }

    /// <summary>
    /// Takes one due order under SKIP LOCKED (status null = any in flight), applies the claim and pushes
    /// its next check out by a lease, and commits, so another worker leaves it alone during the provider call.
    /// </summary>
    private async Task<PaymentOrder?> Claim(PaymentOrderStatus? status, CancellationToken ct, Action<PaymentOrder> claim)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = (await db.PaymentOrders.FromSqlRaw("""
            SELECT *, xmin FROM payments_api."PaymentOrders"
            WHERE ({0} = 0 AND "Status" IN (2, 3, 4) AND "NextStatusCheckAt" <= now())
               OR ({0} <> 0 AND "Status" = {0} AND ("NextStatusCheckAt" IS NULL OR "NextStatusCheckAt" <= now()))
            ORDER BY "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
            """, (int)(status ?? 0)).ToListAsync(ct)).FirstOrDefault();
        if (order is null)
            return null;
        claim(order);
        order.NextStatusCheckAt = DateTime.UtcNow + Lease;
        order.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return order;
    }

    private async Task Apply(PaymentOrder order, ProviderResult result, bool submitting, CancellationToken ct)
    {
        switch (result.State)
        {
            case ProviderState.Successful or ProviderState.Failed:
                Finish(order, result.State == ProviderState.Successful ? PaymentOrderStatus.Successful : PaymentOrderStatus.Failed,
                    result.ReasonCode, result.ReasonMessage, result.FinancialTransactionId, result.Receipt);
                break;
            case ProviderState.NotSent when submitting:
                order.Status = PaymentOrderStatus.Received;
                order.ReasonCode = result.ReasonCode;
                order.ReasonMessage = Trim(result.ReasonMessage, 500);
                if (++order.StatusChecks >= MaxSubmitAttempts)
                    Finish(order, PaymentOrderStatus.Failed, result.ReasonCode, result.ReasonMessage);
                else
                    order.NextStatusCheckAt = DateTime.UtcNow + Backoff[order.StatusChecks - 1];
                break;
            default:
                order.Status = result.State switch
                {
                    ProviderState.Accepted => PaymentOrderStatus.Submitted,
                    ProviderState.Pending => PaymentOrderStatus.Pending,
                    ProviderState.NotFound => PaymentOrderStatus.PendingUnknown,
                    _ => order.Status,
                };
                if (submitting)
                    order.StatusChecks = 0;
                Schedule(order);
                break;
        }

        order.UpdatedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A callback-driven check or a forced check got there first; its result stands.
            logger.LogInformation("Payment {Id} was updated concurrently; this result was dropped", order.Id);
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>The next status check by back-off, or none once the cut-off passes (the payment then needs reconciliation).</summary>
    private void Schedule(PaymentOrder order)
    {
        var next = DateTime.UtcNow + Backoff[Math.Min(order.StatusChecks, Backoff.Length - 1)];
        if (next - order.CreatedAt <= Cutoff)
        {
            order.NextStatusCheckAt = next;
            return;
        }
        order.NextStatusCheckAt = null;
        logger.LogError("Payment {Id} ({ExternalRef}) is still {Status} after {Cutoff}; it needs reconciliation",
            order.Id, order.ExternalRef, order.Status, Cutoff);
    }

    private void Finish(PaymentOrder order, PaymentOrderStatus status, string code, string message, string transactionId = "", string receipt = "")
    {
        var now = DateTime.UtcNow;
        order.Status = status;
        order.ReasonCode = Trim(code, 60);
        order.ReasonMessage = Trim(message, 500);
        order.FinancialTransactionId = Trim(transactionId, 100);
        order.Receipt = Trim(receipt, 200);
        order.CompletedAt = now;
        order.NextStatusCheckAt = null;

        var success = status == PaymentOrderStatus.Successful;
        var eventId = Guid.NewGuid();
        object payload = success
            ? new PaymentCompleted(eventId, order.CorrelationId, order.OrganisationId, order.ExternalRef, order.Provider, order.Id,
                order.Amount, order.Currency, order.FinancialTransactionId, order.Receipt, now)
            : new PaymentFailed(eventId, order.CorrelationId, order.OrganisationId, order.ExternalRef, order.Provider, order.Id,
                order.ReasonCode, order.ReasonMessage, now);
        var type = success ? "payment.completed" : "payment.failed";
        outbox.Enqueue(order.OrganisationId, type, MessagingNames.PaymentsExchange, $"{type}.{order.Provider.ToLowerInvariant()}",
            payload, order.ExternalRef, order.CorrelationId, eventId);

        if (ConfigurationCodes.Contains(code))
            logger.LogCritical("OPS ALERT: {Provider} account {AccountId} is misconfigured; {ExternalRef} failed with {Code}: {Message}",
                order.Provider, order.ProviderAccountId, order.ExternalRef, code, message);
        if (code is "NOT_ENOUGH_FUNDS" or "PAYER_LIMIT_REACHED")
            logger.LogCritical("OPS ALERT: {Provider} float for organisation {OrganisationId} cannot pay {ExternalRef}: {Code}",
                order.Provider, order.OrganisationId, order.ExternalRef, code);
    }

    private IPaymentProvider Provider(PaymentOrder order) => providers.Single(p => p.Name == order.Provider);

    private async Task<ProviderContext> Context(PaymentOrder order)
    {
        var account = await db.ProviderAccounts.AsNoTracking().FirstAsync(a => a.Id == order.ProviderAccountId);
        return new(account, accounts.Secrets(account), order.CorrelationId, order.Id);
    }

    private static string Trim(string text, int length) => text.Length > length ? text[..length] : text;
}
