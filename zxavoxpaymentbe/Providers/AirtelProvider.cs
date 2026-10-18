using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using zxavoxpaymentbe.Entities;

namespace zxavoxpaymentbe.Providers;

/// <summary>
/// Airtel Money Uganda Disbursement v2 (plan 11.7, ZD-181). transaction.id = ProviderReferenceId, so a
/// repeated submission is refused as a duplicate; the wallet PIN is RSA-encrypted with Airtel's key.
/// </summary>
public class AirtelProvider(ProviderHttp http, IMemoryCache cache) : IPaymentProvider
{
    private static readonly string[] Unknown = ["TA", "DP00900001000", "DP00900001014", "ESB000001", "ESB000004", "ESB000008", "ESB000014", "0000900"];
    private static readonly string[] Duplicate = ["DP00900001017", "ESB000041"];
    private static readonly string[] NotFound = ["DP00900001015", "ESB000045"];

    public string Name => "AIRTEL";

    /// <summary>Airtel's transaction.id for an order: the provider reference without dashes.</summary>
    public static string TransactionId(PaymentOrder order) => order.ProviderReferenceId.ToString("N");

    public async Task<ProviderResult> Submit(ProviderContext context, PaymentOrder order, CancellationToken ct)
    {
        var (token, tokenError) = await Token(context, ct);
        if (token is null)
            return tokenError!;
        var key = await PublicKey(context, token, ct);
        if (key is null)
            return new(ProviderState.NotSent, "PROVIDER_UNAVAILABLE", "Airtel encryption key could not be fetched");

        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
        var amount = context.Account.Currency == "UGX" ? decimal.Round(order.Amount, 0) : order.Amount;
        var payload = JsonSerializer.Serialize(new
        {
            payee = new { msisdn = Local(order.PayeeId), currency = context.Account.Currency, name = order.PayeeName },
            reference = order.ExternalRef,
            pin = Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(context.Secrets["pin"]), RSAEncryptionPadding.Pkcs1)),
            transaction = new { amount, id = TransactionId(order), type = context.Account.TransactionType },
        });
        var request = Request(context, token, HttpMethod.Post, "/standard/v2/disbursements/");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        if (context.Account.MessageSigningEnabled)
            Sign(request, payload, rsa);

        var response = await http.Send(context, "disbursement", request, ct);
        return response.Status switch
        {
            null or 408 or >= 500 => new(ProviderState.Unknown, "PROVIDER_UNAVAILABLE", response.Body),
            401 => new(ProviderState.Failed, "PROVIDER_AUTH_FAILED", "Airtel refused the token"),
            _ => Outcome(response.Body, submitting: true),
        };
    }

    public async Task<ProviderResult> Status(ProviderContext context, PaymentOrder order, CancellationToken ct)
    {
        var (token, _) = await Token(context, ct);
        if (token is null)
            return new(ProviderState.Unknown);
        var response = await http.Send(context, "disbursement-enquiry", Request(context, token, HttpMethod.Get,
            $"/standard/v2/disbursements/{TransactionId(order)}?transactionType={context.Account.TransactionType}"), ct);
        return response.Status switch
        {
            404 => new(ProviderState.NotFound),
            200 => Outcome(response.Body, submitting: false),
            _ => new(ProviderState.Unknown),
        };
    }

    public async Task<LookupResult> Lookup(ProviderContext context, string msisdn, CancellationToken ct)
    {
        var (token, tokenError) = await Token(context, ct);
        if (token is null)
            return new(false, "", tokenError!.ReasonMessage);
        var response = await http.Send(context, "user-enquiry", Request(context, token, HttpMethod.Get, $"/standard/v1/users/{Local(msisdn)}"), ct);
        var root = ProviderJson.Parse(response.Body);
        if (response.Status != 200 || ProviderJson.Text(root, "data.first_name").Length == 0)
            return new(false, "", $"The provider lookup failed ({response.Status?.ToString() ?? "no answer"})");
        if (root.GetProperty("data").TryGetProperty("is_barred", out var barred) && barred.ValueKind == JsonValueKind.True)
            return new(false, "", "The number is barred");
        return new(true, $"{ProviderJson.Text(root, "data.first_name")} {ProviderJson.Text(root, "data.last_name")}".Trim(), "");
    }

    public async Task<string?> Test(ProviderContext context, CancellationToken ct) => (await Token(context, ct)).Error?.ReasonMessage;

    /// <summary>Maps a disbursement or enquiry answer by transaction status and response code (plan 11.7).</summary>
    private static ProviderResult Outcome(string body, bool submitting)
    {
        var root = ProviderJson.Parse(body);
        var status = ProviderJson.Text(root, "data.transaction.status");
        var code = ProviderJson.Text(root, "status.response_code") is { Length: > 0 } response ? response : ProviderJson.Text(root, "status.result_code");
        var message = ProviderJson.Text(root, "data.transaction.message") is { Length: > 0 } text ? text : ProviderJson.Text(root, "status.message");
        if (status == "TS" || code == "DP00900001001")
            return new(ProviderState.Successful, FinancialTransactionId: ProviderJson.Text(root, "data.transaction.airtel_money_id"));
        if (status == "TIP" || code == "DP00900001006")
            return new(ProviderState.Pending);
        if (Duplicate.Contains(code))
            return new(submitting ? ProviderState.Accepted : ProviderState.Unknown);
        if (NotFound.Contains(code))
            return new(ProviderState.NotFound);
        if (Unknown.Contains(status) || Unknown.Contains(code))
            return new(ProviderState.Unknown, code, message);
        if (code == "DP00900001007")
            return new(ProviderState.Failed, "NOT_ENOUGH_FUNDS", $"{code}: {message}");
        if (status == "TF" || code.Length > 0)
            return new(ProviderState.Failed, code.Length > 0 ? code : "TF", message);
        return new(ProviderState.Unknown);
    }

    /// <summary>Optional message signing: AES-256-CBC of the payload as x-signature, the key and IV RSA-encrypted as x-key.</summary>
    private static void Sign(HttpRequestMessage request, string payload, RSA rsa)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        var signature = aes.EncryptCbc(Encoding.UTF8.GetBytes(payload), aes.IV, PaddingMode.PKCS7);
        var keyAndIv = $"{Convert.ToBase64String(aes.Key)}:{Convert.ToBase64String(aes.IV)}";
        request.Headers.Add("x-signature", Convert.ToBase64String(signature));
        request.Headers.Add("x-key", Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(keyAndIv), RSAEncryptionPadding.Pkcs1)));
    }

    private async Task<(string? Token, ProviderResult? Error)> Token(ProviderContext context, CancellationToken ct)
    {
        var key = $"airtel-token:{context.Account.Id}:{context.Account.UpdatedAt.Ticks}";
        if (cache.TryGetValue(key, out string? cached))
            return (cached, null);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{context.Account.BaseUrl}/auth/oauth2/token")
        {
            Content = JsonContent.Create(new { client_id = context.Secrets["clientId"], client_secret = context.Secrets["clientSecret"], grant_type = "client_credentials" }),
        };
        var response = await http.Send(context, "token", request, ct);
        if (response.Status is 400 or 401 or 403)
            return (null, new(ProviderState.Failed, "PROVIDER_AUTH_FAILED", "Airtel refused the client id or secret"));
        if (response.Status != 200)
            return (null, new(ProviderState.NotSent, "PROVIDER_UNAVAILABLE", $"Airtel token call failed ({response.Status?.ToString() ?? "no answer"})"));

        var root = ProviderJson.Parse(response.Body);
        var seconds = int.TryParse(ProviderJson.Text(root, "expires_in"), out var s) ? s : 180;
        var token = ProviderJson.Text(root, "access_token");
        cache.Set(key, token, TimeSpan.FromSeconds(Math.Max(10, seconds - 30)));
        return (token, null);
    }

    /// <summary>Airtel's RSA public key, cached until its valid_upto (at most a day).</summary>
    private async Task<string?> PublicKey(ProviderContext context, string token, CancellationToken ct)
    {
        var cacheKey = $"airtel-rsa:{context.Account.Id}:{context.Account.UpdatedAt.Ticks}";
        if (cache.TryGetValue(cacheKey, out string? cached))
            return cached;
        var response = await http.Send(context, "encryption-keys", Request(context, token, HttpMethod.Get, "/v1/rsa/encryption-keys"), ct);
        var root = ProviderJson.Parse(response.Body);
        var key = ProviderJson.Text(root, "data.key");
        if (response.Status != 200 || key.Length == 0)
            return null;
        var until = DateTime.TryParseExact(ProviderJson.Text(root, "data.valid_upto"), "dd-MM-yyyy HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var validUpto) ? validUpto : DateTime.UtcNow.AddHours(1);
        var lifetime = until - DateTime.UtcNow - TimeSpan.FromMinutes(5);
        cache.Set(cacheKey, key, lifetime < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : lifetime > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : lifetime);
        return key;
    }

    private static HttpRequestMessage Request(ProviderContext context, string token, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, context.Account.BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Country", context.Account.Country);
        request.Headers.Add("X-Currency", context.Account.Currency);
        request.Headers.Accept.ParseAdd("*/*");
        return request;
    }

    /// <summary>Airtel wants the number without the country code: 256772123456 becomes 772123456.</summary>
    private static string Local(string msisdn) => msisdn.StartsWith("256") && msisdn.Length == 12 ? msisdn[3..] : msisdn;
}
