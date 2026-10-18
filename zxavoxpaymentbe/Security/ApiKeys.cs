using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using zxavoxpaymentbe.Entities;

namespace zxavoxpaymentbe.Security;

/// <summary>API keys are random 256-bit values, so a plain SHA-256 is enough to store them safely.</summary>
public static class ApiKeys
{
    public const string Header = "X-Api-Key";

    public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    /// <summary>Registers or re-keys the client named in PAY_BOOTSTRAP_CLIENT with PAY_BOOTSTRAP_API_KEY, when both are set.</summary>
    public static async Task Bootstrap(PaymentsDbContext db, string? name, string? key)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(key))
            return;
        var client = await db.ApiClients.FirstOrDefaultAsync(c => c.Name == name);
        if (client is null)
            db.ApiClients.Add(client = new ApiClient { Name = name, CreatedAt = DateTime.UtcNow });
        client.KeyHash = Hash(key);
        client.IsActive = true;
        await db.SaveChangesAsync();
    }
}
