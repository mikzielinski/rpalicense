using System.Security.Cryptography;
using System.Text;

namespace Ops.Runtime.Seed;

internal static class Crypto
{
    internal static byte[] DeriveAesKey(string runtimeToken, string pepper)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes($"{runtimeToken}:{pepper}"));
    }

    internal static string Canonical(CatalogEntry entry)
    {
        var hosts = entry.Hosts.Count == 0
            ? string.Empty
            : string.Join(",", entry.Hosts.Select(h => h.Trim().ToUpperInvariant()).OrderBy(h => h, StringComparer.Ordinal));

        return string.Join("|",
            entry.TokenId.Trim(),
            entry.Owner.Trim(),
            entry.ValidToUtc.Trim(),
            entry.Enabled ? "1" : "0",
            hosts,
            entry.Blob.Trim(),
            entry.Nonce.Trim(),
            entry.Tag.Trim());
    }

    internal static bool VerifySeal(string canonical, string signatureB64, string publicKeyPem)
    {
        try
        {
            var data = Encoding.UTF8.GetBytes(canonical);
            var signature = Convert.FromBase64String(signatureB64);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    internal static string Decrypt(string cipherB64, string nonceB64, string tagB64, byte[] key)
    {
        var cipher = Convert.FromBase64String(cipherB64);
        var nonce = Convert.FromBase64String(nonceB64);
        var tag = Convert.FromBase64String(tagB64);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    internal static string Encrypt(string plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key);
        aes.Encrypt(nonce, plain, cipher, tag);
        return $"{Convert.ToBase64String(cipher)}.{Convert.ToBase64String(nonce)}.{Convert.ToBase64String(tag)}";
    }

    internal static string HashToken(string runtimeToken, string pepper)
    {
        using var sha = SHA256.Create();
        var digest = sha.ComputeHash(Encoding.UTF8.GetBytes($"{runtimeToken}:{pepper}"));
        return Convert.ToBase64String(digest);
    }
}
