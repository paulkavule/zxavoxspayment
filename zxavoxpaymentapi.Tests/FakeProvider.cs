using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace zxavoxpaymentapi.Tests;

/// <summary>The provider side of every test: routes each request to the script registered for its host.</summary>
public sealed class FakeProvider : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, FakeMtn> hosts = new();

    public FakeMtn Mtn()
    {
        var mtn = new FakeMtn($"mtn-{Guid.NewGuid():N}.test");
        hosts[mtn.Host] = mtn;
        return mtn;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        await hosts[request.RequestUri!.Host].Answer(request);

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
