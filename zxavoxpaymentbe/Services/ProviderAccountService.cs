using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using zxavoxpaymentbe.Entities;
using zxavoxpaymentbe.Security;

namespace zxavoxpaymentbe.Services;

public record ProviderAccountInput(
    int? OrganisationId, string Provider, string BaseUrl, string Country, string Currency, string DefaultMethod,
    string TargetEnvironment, string TransactionType, bool MessageSigningEnabled, bool IsActive, Dictionary<string, string>? Secrets);

/// <summary>An account as the API shows it: secret names only, never values.</summary>
public record ProviderAccountView(
    int Id, int? OrganisationId, string Provider, string BaseUrl, string Country, string Currency, string DefaultMethod,
    string TargetEnvironment, string TransactionType, bool MessageSigningEnabled, bool IsActive, IReadOnlyList<string> SecretsSet,
    bool InEffect, DateTime UpdatedAt);

/// <summary>
/// Provider credentials (ZD-338, plan 11.4). An organisation's active account for a provider takes
/// precedence over the platform account (OrganisationId null). Secrets are write-only: an update
/// replaces only the secrets it names and keeps the rest.
/// </summary>
public class ProviderAccountService(PaymentsDbContext db, SecretCipher cipher)
{
    public static readonly Dictionary<string, string[]> RequiredSecrets = new()
    {
        ["MTN"] = ["subscriptionKey", "apiUser", "apiKey"],
        ["AIRTEL"] = ["clientId", "clientSecret", "pin"],
        ["UTILITY"] = [],
    };

    /// <summary>The organisation's accounts and the platform accounts, flagging which one each provider would use.</summary>
    public async Task<IReadOnlyList<ProviderAccountView>> List(int? organisationId)
    {
        var rows = await db.ProviderAccounts.AsNoTracking()
            .Where(a => a.OrganisationId == null || a.OrganisationId == organisationId)
            .OrderBy(a => a.Provider).ThenBy(a => a.OrganisationId).ToListAsync();
        var inEffect = rows.Where(a => a.IsActive).GroupBy(a => a.Provider)
            .Select(g => g.OrderByDescending(a => a.OrganisationId.HasValue).First().Id).ToHashSet();
        return rows.Select(a => View(a, inEffect.Contains(a.Id))).ToList();
    }

    public async Task<(ProviderAccountView? Account, string? Error)> Create(ProviderAccountInput input)
    {
        var provider = input.Provider.Trim().ToUpperInvariant();
        if (await db.ProviderAccounts.AnyAsync(a => a.OrganisationId == input.OrganisationId && a.Provider == provider))
            return (null, "An account for this provider already exists; update it instead");
        var row = new ProviderAccount { OrganisationId = input.OrganisationId, Provider = provider, CreatedAt = DateTime.UtcNow };
        var error = Apply(row, input, new Dictionary<string, string>());
        if (error is not null)
            return (null, error);
        db.ProviderAccounts.Add(row);
        await db.SaveChangesAsync();
        return (View(row, false), null);
    }

    public async Task<(ProviderAccountView? Account, string? Error)> Update(int id, ProviderAccountInput input)
    {
        var row = await db.ProviderAccounts.FirstOrDefaultAsync(a => a.Id == id);
        if (row is null)
            return (null, null);
        var error = Apply(row, input, Secrets(row));
        if (error is not null)
            return (null, error);
        await db.SaveChangesAsync();
        return (View(row, false), null);
    }

    /// <summary>The account a new payment of the organisation uses for the provider, or null (NO_PROVIDER_ACCOUNT).</summary>
    public Task<ProviderAccount?> Resolve(int organisationId, string provider) =>
        db.ProviderAccounts.AsNoTracking()
            .Where(a => a.IsActive && a.Provider == provider && (a.OrganisationId == organisationId || a.OrganisationId == null))
            .OrderBy(a => a.OrganisationId == null).FirstOrDefaultAsync();

    public Dictionary<string, string> Secrets(ProviderAccount account)
    {
        if(account.Secrets.Length == 0)
        return new();
        
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(cipher.Decrypt(account.Secrets))!;
        return result;
    }
        

    private string? Apply(ProviderAccount row, ProviderAccountInput input, Dictionary<string, string> secrets)
    {
        if (!RequiredSecrets.TryGetValue(row.Provider, out var required))
            return "Provider must be MTN, AIRTEL or UTILITY";
        if (!Uri.TryCreate(input.BaseUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            return "BaseUrl must be an absolute https URL";
        if (input.DefaultMethod is not ("DEPOSIT" or "TRANSFER"))
            return "DefaultMethod must be DEPOSIT or TRANSFER";
        foreach (var (name, value) in input.Secrets ?? [])
            if (!string.IsNullOrWhiteSpace(value))
                secrets[name] = value;
        var missing = required.Where(r => !secrets.ContainsKey(r)).ToList();
        if (missing.Count > 0)
            return $"Missing secrets: {string.Join(", ", missing)}";

        row.BaseUrl = input.BaseUrl.TrimEnd('/');
        row.Country = input.Country;
        row.Currency = input.Currency;
        row.DefaultMethod = input.DefaultMethod;
        row.TargetEnvironment = input.TargetEnvironment;
        row.TransactionType = input.TransactionType;
        row.MessageSigningEnabled = input.MessageSigningEnabled;
        row.IsActive = input.IsActive;
        row.Secrets = secrets.Count == 0 ? string.Empty : cipher.Encrypt(JsonSerializer.Serialize(secrets));
        row.UpdatedAt = DateTime.UtcNow;
        return null;
    }

    private ProviderAccountView View(ProviderAccount a, bool inEffect) => new(
        a.Id, a.OrganisationId, a.Provider, a.BaseUrl, a.Country, a.Currency, a.DefaultMethod, a.TargetEnvironment, a.TransactionType,
        a.MessageSigningEnabled, a.IsActive, Secrets(a).Keys.Order().ToList(), inEffect, a.UpdatedAt);
}
