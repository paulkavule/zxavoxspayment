using System.Diagnostics;
using System.Text.RegularExpressions;
using zxavoxpaymentbe.Entities;

namespace zxavoxpaymentbe.Providers;

/// <summary>A provider's answer; Status is null when no answer came (timeout or network error).</summary>
public record ProviderResponse(int? Status, string Body);

/// <summary>
/// Sends provider requests and records each as a ProviderCall with masked bodies. Headers are never
/// recorded, since they carry the credentials. The call is saved at once, so it survives a later failure.
/// </summary>
public partial class ProviderHttp(HttpClient http, PaymentsDbContext db)
{
    public async Task<ProviderResponse> Send(ProviderContext context, string operation, HttpRequestMessage request, CancellationToken ct)
    {
        var requestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        var watch = Stopwatch.StartNew();
        ProviderResponse response;
        try
        {
            using var reply = await http.SendAsync(request, ct);
            response = new((int)reply.StatusCode, await reply.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            response = new(null, ex.Message);
        }

        db.ProviderCalls.Add(new ProviderCall
        {
            PaymentOrderId = context.PaymentOrderId, Provider = context.Account.Provider, Operation = operation, HttpStatus = response.Status,
            DurationMs = (int)watch.ElapsedMilliseconds, Request = $"{request.Method} {request.RequestUri?.AbsolutePath} {Mask(requestBody)}",
            Response = Mask(response.Body.Length > 4000 ? response.Body[..4000] : response.Body), CorrelationId = context.CorrelationId, At = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(CancellationToken.None);
        return response;
    }

    /// <summary>Hides tokens, PINs and secrets, and all but the ends of long numbers such as MSISDNs.</summary>
    public static string Mask(string text) =>
        LongNumber().Replace(Secret().Replace(text, m => $"\"{m.Groups[1].Value}\":\"***\""), m => $"{m.Value[..3]}***{m.Value[^2..]}");

    [GeneratedRegex("\"(pin|access_token|client_secret|token|key)\"\\s*:\\s*\"[^\"]*\"", RegexOptions.IgnoreCase)]
    private static partial Regex Secret();

    [GeneratedRegex(@"\d{9,}")]
    private static partial Regex LongNumber();
}
