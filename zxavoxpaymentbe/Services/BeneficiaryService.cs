using zxavoxpaymentbe.Entities;
using zxavoxpaymentbe.Providers;

namespace zxavoxpaymentbe.Services;

public record BeneficiaryLookupResponse(bool Active, string Name, string Reference, string Error);

/// <summary>Who holds a mobile money number (ZD-196), through the organisation's account in effect. Every lookup is logged, masked.</summary>
public class BeneficiaryService(PaymentsDbContext db, ProviderAccountService accounts, IEnumerable<IPaymentProvider> providers)
{
    public async Task<BeneficiaryLookupResponse> Lookup(int organisationId, string provider, string msisdn, string correlationId, CancellationToken ct)
    {
        var account = await accounts.Resolve(organisationId, provider);
        var adapter = providers.FirstOrDefault(p => p.Name == provider);
        var result = account is null || adapter is null
            ? new LookupResult(false, "", $"No active {provider} account for the organisation or the platform")
            : await adapter.Lookup(new ProviderContext(account, accounts.Secrets(account), correlationId, null), msisdn, ct);

        var log = new BeneficiaryLookupLog
        {
            OrganisationId = organisationId, Provider = provider, MaskedMsisdn = ProviderHttp.Mask(msisdn), Active = result.Active,
            Name = result.Name.Length > 100 ? result.Name[..100] : result.Name,
            Error = result.Error.Length > 200 ? result.Error[..200] : result.Error, LookedUpAt = DateTime.UtcNow,
        };
        db.BeneficiaryLookups.Add(log);
        await db.SaveChangesAsync(ct);
        return new(result.Active, result.Name, $"LK-{log.Id}", result.Error);
    }
}
