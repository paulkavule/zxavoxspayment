using System.Net;

namespace zxavoxpaymentapi.Tests;

/// <summary>ZD-338: only a known, active client key reaches the API; health stays open for probes.</summary>
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
    public async Task Health_needs_no_key_and_ready_checks_the_database_and_broker()
    {
        var client = fixture.Client(null);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Contains("\"rabbitMq\":true", await ready.Content.ReadAsStringAsync());
    }
}
