using System.Security.Cryptography;
using System.Text;

namespace zxavoxpaymentbe.Security;

/// <summary>
/// AES-GCM for provider secrets at rest. The 256-bit key (base64 or hex) comes from PAY_SECRETS_KEY,
/// never the database; without it the service does not start, since it could not use any provider.
/// </summary>
public class SecretCipher
{
    public const string KeyVariable = "PAY_SECRETS_KEY";
    private const string Prefix = "aesgcm.v1.";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] key;

    public SecretCipher(string? configuredKey)
    {
        var text = configuredKey?.Trim() ?? string.Empty;
        key = text.Length == 64 ? Convert.FromHexString(text) : Convert.FromBase64String(text);
        if (key.Length != 32)
            throw new InvalidOperationException($"{KeyVariable} must be a 256-bit key in base64 or hex.");
    }

    public string Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Prefix + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    /// <summary>Throws on a wrong key or a tampered value: a payment must not go out with unreadable credentials.</summary>
    public string Decrypt(string ciphertext)
    {
        if (!ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("The value is not in the expected encrypted format.");
        var payload = Convert.FromBase64String(ciphertext[Prefix.Length..]);
        var plain = new byte[payload.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize + TagSize), payload.AsSpan(NonceSize, TagSize), plain);
        return Encoding.UTF8.GetString(plain);
    }
}
