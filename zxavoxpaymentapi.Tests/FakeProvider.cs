using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace zxavoxpaymentapi.Tests;

/// <summary>The provider side of every test: routes each request to the script registered for its host.</summary>
public sealed class FakeProvider : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> hosts = new();

    public FakeMtn Mtn()
    {
        var mtn = new FakeMtn($"mtn-{Guid.NewGuid():N}.test");
        hosts[mtn.Host] = mtn.Answer;
        return mtn;
    }

    public FakeAirtel Airtel()
    {
        var airtel = new FakeAirtel($"airtel-{Guid.NewGuid():N}.test");
        hosts[airtel.Host] = airtel.Answer;
        return airtel;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        await hosts[request.RequestUri!.Host](request);

    // The client factory disposes expired handlers; this one is shared for the whole run.
    protected override void Dispose(bool disposing) { }
}

/// <summary>A scripted MTN Disbursement API that records what it was asked.</summary>
public sealed class FakeMtn(string host)
{
    public string Host => host;
    public string BaseUrl => $"https://{host}/disbursement";
    public ConcurrentQueue<(string Method, string Path, string ReferenceId, string Body)> Calls { get; } = new();
    public Func<HttpResponseMessage> Submit { get; set; } = () => new(HttpStatusCode.Accepted);
    public Func<HttpResponseMessage> Status { get; set; } = () => Json(200, """{"status":"PENDING"}""");
    public Func<HttpResponseMessage> Active { get; set; } = () => Json(200, """{"result":true}""");
    public Func<HttpResponseMessage> UserInfo { get; set; } = () => Json(200, """{"given_name":"Jane","family_name":"Akello"}""");

    public int Count(string pathStart, string method = "POST") => Calls.Count(c => c.Method == method && c.Path.StartsWith(pathStart));

    public static HttpResponseMessage Json(int status, string body) =>
        new((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public async Task<HttpResponseMessage> Answer(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath["/disbursement".Length..];
        var reference = request.Headers.TryGetValues("X-Reference-Id", out var values) ? values.Single() : "";
        Calls.Enqueue((request.Method.Method, path, reference, request.Content is null ? "" : await request.Content.ReadAsStringAsync()));
        return path switch
        {
            "/token/" => Json(200, """{"access_token":"tok","token_type":"access_token","expires_in":3600}"""),
            "/v1_0/deposit" or "/v1_0/transfer" => Submit(),
            _ when path.EndsWith("/active") => Active(),
            _ when path.EndsWith("/basicuserinfo") => UserInfo(),
            _ => Status(),
        };
    }
}

/// <summary>A scripted Airtel Uganda API holding its own RSA key pair, so a test can read the PIN as Airtel would.</summary>
public sealed class FakeAirtel(string host)
{
    private readonly System.Security.Cryptography.RSA rsa = System.Security.Cryptography.RSA.Create(1024);

    public string Host => host;
    public string BaseUrl => $"https://{host}";
    public ConcurrentQueue<(string Method, string Path, string Body)> Calls { get; } = new();
    public Func<HttpResponseMessage> Disburse { get; set; } = () => Transaction(200, "TIP", "DP00900001006");
    public Func<HttpResponseMessage> Enquiry { get; set; } = () => Transaction(200, "TIP", "DP00900001006");
    public Func<HttpResponseMessage> User { get; set; } = () => FakeMtn.Json(200,
        """{"data":{"first_name":"John","last_name":"Okello","is_barred":false},"status":{"code":"200","success":true}}""");

    public int Count(string pathStart, string method = "POST") => Calls.Count(c => c.Method == method && c.Path.StartsWith(pathStart));

    public string DecryptPin(string encrypted) =>
        Encoding.UTF8.GetString(rsa.Decrypt(Convert.FromBase64String(encrypted), System.Security.Cryptography.RSAEncryptionPadding.Pkcs1));

    public static HttpResponseMessage Transaction(int status, string transactionStatus, string code, string moneyId = "") => FakeMtn.Json(status,
        $$$"""{"data":{"transaction":{"id":"x","status":"{{{transactionStatus}}}","airtel_money_id":"{{{moneyId}}}","message":"m"}},"status":{"response_code":"{{{code}}}","success":true}}""");

    public async Task<HttpResponseMessage> Answer(HttpRequestMessage request)
    {
        var path = request.RequestUri!.PathAndQuery;
        Calls.Enqueue((request.Method.Method, path, request.Content is null ? "" : await request.Content.ReadAsStringAsync()));
        return path switch
        {
            "/auth/oauth2/token" => FakeMtn.Json(200, """{"access_token":"tok","expires_in":"180","token_type":"bearer"}"""),
            "/v1/rsa/encryption-keys" => FakeMtn.Json(200,
                $$$"""{"data":{"key_id":1,"key":"{{{Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo())}}}","valid_upto":"01-01-2099 00:00:00"}}"""),
            "/standard/v2/disbursements/" => Disburse(),
            _ when path.StartsWith("/standard/v1/users/") => User(),
            _ => Enquiry(),
        };
    }
}
