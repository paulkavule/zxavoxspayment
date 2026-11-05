using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using zxavoxpaymentbe.Entities;

namespace zxavoxpaymentbe.Providers;

/// <summary>
/// MTN MoMo Disbursement (plan 11.6, ZD-160, ZD-260). Pays by Deposit-V1 or Transfer per the order's
/// method, keyed by X-Reference-Id = ProviderReferenceId, and reads status from the same method's endpoint.
/// </summary>
public class MtnProvider(ProviderHttp http, IMemoryCache cache, IConfiguration configuration) : IPaymentProvider
{
    public string Name => "MTN";

    public async Task<ProviderResult> Submit(ProviderContext context, PaymentOrder order, CancellationToken ct)
    {
        var (token, tokenError) = await Token(context, ct);
        if (token is null)
            return tokenError!;

        var request = Request(context, token, HttpMethod.Post, $"/v1_0/{Path(order)}");
        request.Headers.Add("X-Reference-Id", order.ProviderReferenceId.ToString());
        if (configuration["Payments:CallbackBaseUrl"] is { Length: > 0 } callbackBase)
            request.Headers.Add("X-Callback-Url", $"{callbackBase.TrimEnd('/')}/callbacks/mtn/{order.ProviderReferenceId}");
        request.Content = JsonContent.Create(new
        {
            amount = order.Amount.ToString("0.##", CultureInfo.InvariantCulture), 
            currency = context.Account.Currency, 
            externalId = order.ExternalRef,
            payee = new { partyIdType = "MSISDN", partyId = order.PayeeId }, payerMessage = order.PayerMessage, payeeNote = order.PayeeNote,
        });

        var response = await http.Send(context, $"{Path(order)}", request, ct);
        var (code, message) = Reason(response.Body);
        return response.Status switch
        {
            null or >= 500 => new(ProviderState.Unknown, "PROVIDER_UNAVAILABLE", response.Body),
            202 => new(ProviderState.Accepted),
            409 when code == "RESOURCE_ALREADY_EXIST" => new(ProviderState.Accepted),
            401 or 403 => new(ProviderState.Failed, "PROVIDER_AUTH_FAILED", message),
            400 => new(ProviderState.Failed, code.Length > 0 ? code : "INVALID_REQUEST", message),
            _ => new(ProviderState.Failed, code.Length > 0 ? code : $"HTTP_{response.Status}", message),
        };
    }

    public async Task<ProviderResult> Status(ProviderContext context, PaymentOrder order, CancellationToken ct)
    {
        var (token, _) = await Token(context, ct);
        if (token is null)
            return new(ProviderState.Unknown);

        var response = await http.Send(context, $"{Path(order)}-status",
            Request(context, token, HttpMethod.Get, $"/v1_0/{Path(order)}/{order.ProviderReferenceId}"), ct);
        if (response.Status == 404)
            return new(ProviderState.NotFound);
        if (response.Status != 200)
            return new(ProviderState.Unknown);

        using var json = JsonDocument.Parse(response.Body);
        var root = json.RootElement;
        var (code, message) = root.TryGetProperty("reason", out var reason) ? Reason(reason) : ("", "");
        return ProviderJson.Text(root, "status") switch
        {
            "SUCCESSFUL" => new(ProviderState.Successful, FinancialTransactionId: ProviderJson.Text(root, "financialTransactionId")),
            "FAILED" => new(ProviderState.Failed, code.Length > 0 ? code : "FAILED", message),
            _ => new(ProviderState.Pending),
        };
    }

    public async Task<LookupResult> Lookup(ProviderContext context, string msisdn, CancellationToken ct)
    {
        var (token, tokenError) = await Token(context, ct);
        if (token is null)
            return new(false, "", tokenError!.ReasonMessage);

        var active = await http.Send(context, "account-holder-active",
            Request(context, token, HttpMethod.Get, $"/v1_0/accountholder/msisdn/{msisdn}/active"), ct);
        if (active.Status != 200)
            return new(false, "", $"The provider lookup failed ({active.Status?.ToString() ?? "no answer"})");
        using (var json = JsonDocument.Parse(active.Body))
            if (!json.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.True)
                return new(false, "", "The number is not an active account");

        var info = await http.Send(context, "basic-user-info",
            Request(context, token, HttpMethod.Get, $"/v1_0/accountholder/msisdn/{msisdn}/basicuserinfo"), ct);
        if (info.Status != 200)
            return new(false, "", $"The provider name lookup failed ({info.Status?.ToString() ?? "no answer"})");
        using var user = JsonDocument.Parse(info.Body);
        var name = $"{ProviderJson.Text(user.RootElement, "given_name")} {ProviderJson.Text(user.RootElement, "family_name")}".Trim();
        return name.Length > 0 ? new(true, name, "") : new(false, "", "The provider returned no name");
    }

    public async Task<string?> Test(ProviderContext context, CancellationToken ct) => (await Token(context, ct)).Error?.ReasonMessage;

    /// <summary>A cached token, or why there is none; keyed by UpdatedAt so new credentials get a new token.</summary>
    private async Task<(string? Token, ProviderResult? Error)> Token(ProviderContext context, CancellationToken ct)
    {
        var key = $"mtn-token:{context.Account.Id}:{context.Account.UpdatedAt.Ticks}";
        if (cache.TryGetValue(key, out string? cached))
            return (cached, null);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{context.Account.BaseUrl}/token/");
        var apiUser = context.Secrets["apiUser"];
        var apiKey = context.Secrets["apiKey"];
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiUser}:{apiKey}")));
        request.Headers.Add("Ocp-Apim-Subscription-Key", context.Secrets["subscriptionKey"]);
        var response = await http.Send(context, "token", request, ct);
        if (response.Status is 401 or 403)
            return (null, new(ProviderState.Failed, "PROVIDER_AUTH_FAILED", "MTN refused the API user or key"));
        if (response.Status != 200)
            return (null, new(ProviderState.NotSent, "PROVIDER_UNAVAILABLE", $"MTN token call failed ({response.Status?.ToString() ?? "no answer"})"));

        using var json = JsonDocument.Parse(response.Body);
        var token = ProviderJson.Text(json.RootElement, "access_token");
        var seconds = json.RootElement.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var s) ? s : 300;
        cache.Set(key, token, TimeSpan.FromSeconds(Math.Max(10, seconds - 60)));
        return (token, null);
    }

    private static HttpRequestMessage Request(ProviderContext context, string token, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, context.Account.BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Target-Environment", context.Account.TargetEnvironment);
        request.Headers.Add("Ocp-Apim-Subscription-Key", context.Secrets["subscriptionKey"]);
        return request;
    }

    private static string Path(PaymentOrder order) => order.Method == "TRANSFER" ? "transfer" : "deposit";

    /// <summary>MTN's ErrorReason, which arrives either as {code, message} or as a bare code string.</summary>
    private static (string Code, string Message) Reason(JsonElement reason) =>
        reason.ValueKind == JsonValueKind.String ? (reason.GetString()!, "") : (ProviderJson.Text(reason, "code"), ProviderJson.Text(reason, "message"));

    private static (string Code, string Message) Reason(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return Reason(json.RootElement);
        }
        catch (JsonException)
        {
            return ("", body.Length > 500 ? body[..500] : body);
        }
    }
}
