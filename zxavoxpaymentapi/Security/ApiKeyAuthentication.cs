using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using zxavoxpaymentbe;
using zxavoxpaymentbe.Security;

namespace zxavoxpaymentapi.Security;

/// <summary>Authenticates a calling system by its X-Api-Key; the client id becomes the NameIdentifier claim.</summary>
public class ApiKeyAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, PaymentsDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeys.Header, out var key) || string.IsNullOrWhiteSpace(key))
            return AuthenticateResult.NoResult();
        var hash = ApiKeys.Hash(key.ToString());
        var client = await db.ApiClients.AsNoTracking().FirstOrDefaultAsync(c => c.KeyHash == hash && c.IsActive);
        if (client is null)
            return AuthenticateResult.Fail("Invalid API key");
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, client.Id.ToString()), new Claim(ClaimTypes.Name, client.Name)], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
