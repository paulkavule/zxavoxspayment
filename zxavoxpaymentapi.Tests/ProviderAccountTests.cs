using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using zxavoxpaymentbe.Services;

namespace zxavoxpaymentapi.Tests;

/// <summary>ZD-338: secrets are encrypted and write-only; an organisation's own account overrides the platform one.</summary>
[Collection(ApiCollection.Name)]
public class ProviderAccountTests(ApiFixture fixture)
{
    private static ProviderAccountInput Mtn(int? org, Dictionary<string, string>? secrets) => new(
        org, "MTN", "https://sandbox.momodeveloper.mtn.com/disbursement", "UG", "EUR", "DEPOSIT", "sandbox", "B2C", false, true, secrets);

    [Fact]
    public async Task Secrets_are_stored_encrypted_never_returned_and_kept_when_an_update_omits_them()
    {
        var client = fixture.Client();
        var org = Random.Shared.Next(1_000, 1_000_000);
        var created = await client.PostAsJsonAsync("/provider-accounts",
            Mtn(org, new() { ["subscriptionKey"] = "sub-SECRET-1", ["apiUser"] = "user-SECRET-2", ["apiKey"] = "key-SECRET-3" }));
        var body = await created.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.DoesNotContain("SECRET", body);
        var id = (await created.Content.ReadFromJsonAsync<ProviderAccountView>())!.Id;

        var updated = await client.PutAsJsonAsync($"/provider-accounts/{id}", Mtn(org, new() { ["apiKey"] = "key-NEW-4" }));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(["apiKey", "apiUser", "subscriptionKey"], (await updated.Content.ReadFromJsonAsync<ProviderAccountView>())!.SecretsSet);

        await using var db = fixture.NewDb();
        var row = await db.ProviderAccounts.SingleAsync(a => a.Id == id);
        Assert.DoesNotContain("SECRET", row.Secrets);
        Assert.DoesNotContain("NEW", row.Secrets);
    }

    [Fact]
    public async Task An_account_without_the_required_secrets_is_refused()
    {
        var response = await fixture.Client().PostAsJsonAsync("/provider-accounts",
            Mtn(Random.Shared.Next(1_000, 1_000_000), new() { ["apiUser"] = "u" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("subscriptionKey", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_organisations_own_account_is_in_effect_over_the_platform_account()
    {
        var client = fixture.Client();
        var secrets = new Dictionary<string, string> { ["clientId"] = "c", ["clientSecret"] = "s", ["pin"] = "1234" };
        var airtel = (int? org) => new ProviderAccountInput(org, "AIRTEL", "https://openapiuat.airtel.ug", "UG", "UGX", "DEPOSIT", "", "B2C", false, true, secrets);
        await client.PostAsJsonAsync("/provider-accounts", airtel(null));
        var org = Random.Shared.Next(1_000, 1_000_000);
        var own = await (await client.PostAsJsonAsync("/provider-accounts", airtel(org))).Content.ReadFromJsonAsync<ProviderAccountView>();

        var other = await client.GetFromJsonAsync<List<ProviderAccountView>>($"/provider-accounts?organisationId={org + 1}");
        var mine = await client.GetFromJsonAsync<List<ProviderAccountView>>($"/provider-accounts?organisationId={org}");
        Assert.True(other!.Single(a => a.Provider == "AIRTEL").InEffect);
        Assert.Equal(own!.Id, mine!.Single(a => a.Provider == "AIRTEL" && a.InEffect).Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/provider-accounts", airtel(null))).StatusCode);
    }
}
