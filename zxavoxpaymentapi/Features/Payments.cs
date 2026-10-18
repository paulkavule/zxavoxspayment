using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using zxavoxpaymentbe;
using zxavoxpaymentbe.Entities;
using zxavoxpaymentbe.Services;

namespace zxavoxpaymentapi.Features;

public class ListPaymentsRequest
{
    [QueryParam] public string? ExternalRef { get; set; }
    [QueryParam] public PaymentOrderStatus? Status { get; set; }
    [QueryParam] public DateTime? From { get; set; }
    [QueryParam] public DateTime? To { get; set; }
}

/// <summary>The caller's payments, newest first, for Zxadocs, reconciliation and operations (ZD-281, ZD-303).</summary>
public class ListPayments(PaymentsDbContext db) : Endpoint<ListPaymentsRequest, List<PaymentOrder>>
{
    public override void Configure() => Get("/payments");

    public override async Task HandleAsync(ListPaymentsRequest req, CancellationToken ct)
    {
        var clientId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var from = req.From?.ToUniversalTime();
        var to = req.To?.ToUniversalTime();
        await Send.OkAsync(await db.PaymentOrders.AsNoTracking()
            .Where(o => o.ClientId == clientId && (req.ExternalRef == null || o.ExternalRef == req.ExternalRef) && (req.Status == null || o.Status == req.Status)
                && (from == null || o.CreatedAt >= from) && (to == null || o.CreatedAt < to))
            .OrderByDescending(o => o.Id).Take(500).ToListAsync(ct), ct);
    }
}

public class GetPayment(PaymentsDbContext db) : EndpointWithoutRequest<PaymentOrder>
{
    public override void Configure() => Get("/payments/{id:long}");

    public override async Task HandleAsync(CancellationToken ct)
    {
        var clientId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var order = await db.PaymentOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == Route<long>("id") && o.ClientId == clientId, ct);
        await Send.ResultAsync(order is null ? Results.NotFound() : Results.Ok(order));
    }
}

public class CheckPaymentStatus(PaymentProcessor processor) : EndpointWithoutRequest<PaymentOrder>
{
    public override void Configure() => Post("/payments/{id:long}/status-check");

    public override async Task HandleAsync(CancellationToken ct)
    {
        var order = await processor.CheckNow(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), Route<long>("id"), ct);
        await Send.ResultAsync(order is null ? Results.NotFound() : Results.Ok(order));
    }
}

/// <summary>MTN's X-Callback-Url target. Public; only a hint to re-query, so it always answers 200 and reveals nothing.</summary>
public class MtnCallback(PaymentProcessor processor) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/callbacks/mtn/{referenceId}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        using var reader = new StreamReader(HttpContext.Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        await processor.Hint("MTN", Route<string>("referenceId")!, body, verified: false, ct);
        await Send.ResultAsync(Results.Ok());
    }
}

public record LookupRequest(int OrganisationId, string Provider, string Msisdn);

public class LookupBeneficiary(BeneficiaryService beneficiaries) : Endpoint<LookupRequest, BeneficiaryLookupResponse>
{
    public override void Configure() => Post("/beneficiaries/lookup");

    public override async Task HandleAsync(LookupRequest req, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(req.Msisdn ?? "", @"^256\d{9}$"))
        {
            await Send.ResultAsync(Results.Problem("msisdn must be 256 followed by 9 digits", statusCode: 400));
            return;
        }
        await Send.OkAsync(await beneficiaries.Lookup(req.OrganisationId, req.Provider.ToUpperInvariant(), req.Msisdn!,
            (string)HttpContext.Items["CorrelationId"]!, ct), ct);
    }
}
