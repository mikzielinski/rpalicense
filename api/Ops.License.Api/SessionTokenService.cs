using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ops.License.Api;

public sealed class SessionTokenService
{
    private readonly ServerSettings _settings;

    public SessionTokenService(ServerSettings settings)
    {
        _settings = settings;
    }

    public string IssueRuntime(string tokenId, string machine)
    {
        return Issue("runtime", new Dictionary<string, string>
        {
            ["tokenId"] = tokenId,
            ["machine"] = machine
        });
    }

    public string IssueOperator(string operatorId)
    {
        return Issue("operator", new Dictionary<string, string>
        {
            ["operatorId"] = operatorId
        });
    }

    public bool TryValidate(string token, string expectedKind, out SessionClaims claims)
    {
        claims = new SessionClaims();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('.', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        try
        {
            var payloadJson = Encoding.UTF8.GetString(FromBase64Url(parts[0]));
            var payload = JsonSerializer.Deserialize<SessionClaims>(payloadJson);
            if (payload is null || payload.Exp <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                return false;
            }

            if (!string.Equals(payload.Kind, expectedKind, StringComparison.Ordinal))
            {
                return false;
            }

            var expectedSig = Sign(parts[0]);
            var actual = FromBase64Url(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(expectedSig, actual))
            {
                return false;
            }

            claims = payload;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string Issue(string kind, Dictionary<string, string> fields)
    {
        var claims = new SessionClaims
        {
            Kind = kind,
            Exp = DateTimeOffset.UtcNow.AddMinutes(_settings.SessionTtlMinutes).ToUnixTimeSeconds()
        };

        if (fields.TryGetValue("tokenId", out var tokenId))
        {
            claims.TokenId = tokenId;
        }

        if (fields.TryGetValue("machine", out var machine))
        {
            claims.Machine = machine;
        }

        if (fields.TryGetValue("operatorId", out var operatorId))
        {
            claims.OperatorId = operatorId;
        }

        var payloadJson = JsonSerializer.Serialize(claims);
        var payloadPart = ToBase64Url(Encoding.UTF8.GetBytes(payloadJson));
        var sigPart = ToBase64Url(Sign(payloadPart));
        return $"{payloadPart}.{sigPart}";
    }

    private byte[] Sign(string payloadPart)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_settings.SessionSigningKey));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadPart));
    }

    private static string ToBase64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        var pad = (4 - (s.Length % 4)) % 4;
        return Convert.FromBase64String(s + new string('=', pad));
    }
}

public sealed class SessionClaims
{
    public string Kind { get; set; } = string.Empty;
    public long Exp { get; set; }
    public string TokenId { get; set; } = string.Empty;
    public string Machine { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;
}
