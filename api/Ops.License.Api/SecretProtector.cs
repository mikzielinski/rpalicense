using System.Security.Cryptography;
using System.Text;

namespace Ops.License.Api;

public sealed class SecretProtector
{
    private readonly byte[] _key;

    public SecretProtector(ServerSettings settings)
    {
        var material = settings.SessionSigningKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(material))
        {
            throw new InvalidOperationException("OPS_SESSION_SIGNING_KEY is required to protect OAuth secrets.");
        }

        _key = SHA256.HashData(Encoding.UTF8.GetBytes(material));
    }

    public string Encrypt(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[bytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, bytes, cipher, tag);

        var packed = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, packed, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, packed, nonce.Length + tag.Length, cipher.Length);
        return Convert.ToBase64String(packed);
    }

    public string Decrypt(string cipherText)
    {
        var packed = Convert.FromBase64String(cipherText);
        if (packed.Length < 29)
        {
            throw new CryptographicException("Invalid protected secret payload.");
        }

        var nonce = packed.AsSpan(0, 12).ToArray();
        var tag = packed.AsSpan(12, 16).ToArray();
        var cipher = packed.AsSpan(28).ToArray();
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    public static string Mask(string? secret) =>
        string.IsNullOrEmpty(secret) ? string.Empty : secret.Length > 4 ? $"••••{secret[^4..]}" : "••••";
}
