using System.Net;

namespace zxavoxpaymentapi.Tests;

/// <summary>ZD-338: only a known, active client key reaches the API; health stays open for probes (ZD-296).</summary>
[Collection(ApiCollection.Name)]
public class ServiceAccessTests(ApiFixture fixture)
{
    [Theory]
    [InlineData(null)]
    [InlineData("not-a-real-key")]
    public async Task A_missing_or_unknown_key_is_refused(string? key)
    {
        var response = await fixture.Client(key).GetAsync("/provider-accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_bootstrapped_key_is_accepted_and_the_correlation_id_is_echoed()
    {
        var client = fixture.Client();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-123");
        var response = await client.GetAsync("/provider-accounts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("corr-123", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task Health_needs_no_key_and_ready_reports_each_check_without_secrets()
    {
        var client = fixture.Client(null);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        // ZD-296: every readiness check is listed with counts only.
        using var body = System.Text.Json.JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        var checks = body.RootElement.GetProperty("checks");
        Assert.Equal(["database", "dead-letters", "outbox", "providers", "rabbitmq", "stuck-payments"],
            checks.EnumerateObject().Select(c => c.Name).Order());
        Assert.Equal("Healthy", checks.GetProperty("rabbitmq").GetProperty("status").GetString());
        Assert.DoesNotContain(ApiFixture.ClientKey, body.RootElement.GetRawText());
        Assert.DoesNotContain("Password", body.RootElement.GetRawText());
    }
}
