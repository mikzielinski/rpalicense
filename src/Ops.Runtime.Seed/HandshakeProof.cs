using System.Security.Cryptography;
using System.Text;

namespace UiPath.System.RoboticSecurity;

internal static class HandshakeProof
{
    internal static string ComputeRuntimeProof(
        string runtimeToken,
        string pepper,
        string challengeId,
        string serverNonce,
        string clientNonce,
        string machine,
        string tokenId)
    {
        var key = Crypto.DeriveAesKey(runtimeToken, pepper);
        return ComputeProof(key, challengeId, serverNonce, clientNonce, machine, tokenId);
    }

    internal static string ComputeOperatorProof(
        string operatorSecret,
        string challengeId,
        string serverNonce,
        string clientNonce,
        string operatorId)
    {
        using var sha = SHA256.Create();
        var key = sha.ComputeHash(Encoding.UTF8.GetBytes(operatorSecret));
        return ComputeProof(key, challengeId, serverNonce, clientNonce, operatorId, operatorId);
    }

    internal static bool VerifyRuntimeProof(
        string runtimeToken,
        string pepper,
        string challengeId,
        string serverNonce,
        string clientNonce,
        string machine,
        string tokenId,
        string proofB64Url)
    {
        var expected = ComputeRuntimeProof(runtimeToken, pepper, challengeId, serverNonce, clientNonce, machine, tokenId);
        return FixedTimeEquals(expected, proofB64Url);
    }

    internal static bool VerifyOperatorProof(
        string operatorSecret,
        string challengeId,
        string serverNonce,
        string clientNonce,
        string operatorId,
        string proofB64Url)
    {
        var expected = ComputeOperatorProof(operatorSecret, challengeId, serverNonce, clientNonce, operatorId);
        return FixedTimeEquals(expected, proofB64Url);
    }

    private static string ComputeProof(
        byte[] key,
        string challengeId,
        string serverNonce,
        string clientNonce,
        string machine,
        string tokenId)
    {
        var message = $"{challengeId}|{serverNonce}|{clientNonce}|{machine}|{tokenId}";
        using var hmac = new HMACSHA256(key);
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Crypto.Base64UrlEncode(signature);
    }

    private static bool FixedTimeEquals(string expectedB64Url, string actualB64Url)
    {
        try
        {
            var expected = Crypto.Base64UrlDecode(expectedB64Url);
            var actual = Crypto.Base64UrlDecode(actualB64Url);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }
}
