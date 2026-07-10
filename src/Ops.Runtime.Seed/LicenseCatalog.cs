using System.Text;
using System.Text.Json;

namespace UiPath.System.RoboticSecurity;

internal static class LicenseCatalog
{
    internal static CatalogEntry ResolveEntry(CatalogDocument catalog, string runtimeToken, string machine)
    {
        var entry = catalog.Entries.FirstOrDefault(e => string.Equals(e.TokenId, runtimeToken, StringComparison.Ordinal));
        if (entry is null)
        {
            throw new InvalidOperationException("boot-0x11");
        }

        if (!entry.Enabled)
        {
            throw new InvalidOperationException("boot-0x12");
        }

        if (!DateTimeOffset.TryParse(entry.ValidToUtc, out var validToUtc))
        {
            throw new InvalidOperationException("boot-0x13");
        }

        if (DateTimeOffset.UtcNow > validToUtc)
        {
            throw new InvalidOperationException("boot-0x14");
        }

        if (entry.Hosts.Count > 0 && !entry.Hosts.Any(h => string.Equals(h.Trim(), machine, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("boot-0x15");
        }

        var canonical = Crypto.Canonical(entry);
        if (!Crypto.VerifySeal(canonical, entry.Seal, BootstrapperSettings.PublicSealKeyPem))
        {
            throw new InvalidOperationException("boot-0x16");
        }

        return entry;
    }

    internal static CatalogDocument ParseCatalog(string sourceBody)
    {
        var body = sourceBody.Trim();
        if (!BootstrapperSettings.SourceUsesJwtEnvelope)
        {
            return JsonSerializer.Deserialize<CatalogDocument>(body, Json.Options)
                ?? throw new InvalidOperationException("boot-0x51");
        }

        return ParseCatalogFromJwt(body);
    }

    internal static RuntimeProfile MaterializeProfile(CatalogEntry entry, string runtimeToken, byte[] key)
    {
        var payloadJson = Crypto.Decrypt(entry.Blob, entry.Nonce, entry.Tag, key);
        var payload = JsonSerializer.Deserialize<SecretPayload>(payloadJson, Json.Options)
            ?? throw new global::System.Security.Cryptography.CryptographicException("boot-0x31");

        if (!DateTimeOffset.TryParse(entry.ValidToUtc, out var validToUtc))
        {
            throw new InvalidOperationException("boot-0x13");
        }

        return new RuntimeProfile
        {
            ApiEndpoint = payload.ApiEndpoint,
            ConnectionString = payload.ConnectionString,
            AgentSystemPrompt = payload.AgentSystemPrompt,
            ValidToUtc = validToUtc,
            Owner = entry.Owner,
            TokenId = runtimeToken
        };
    }

    private static CatalogDocument ParseCatalogFromJwt(string jwt)
    {
        var parts = jwt.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            throw new InvalidOperationException("boot-0x52");
        }

        try
        {
            var headerJson = Encoding.UTF8.GetString(Crypto.Base64UrlDecode(parts[0]));
            using var headerDoc = JsonDocument.Parse(headerJson);
            var alg = headerDoc.RootElement.GetProperty("alg").GetString();
            if (!string.Equals(alg, "HS256", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("boot-0x5A");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("boot-0x5A");
        }

        var signed = $"{parts[0]}.{parts[1]}";
        if (!Crypto.VerifyHs256(signed, parts[2], BootstrapperSettings.EnvelopeSigningKey))
        {
            throw new InvalidOperationException("boot-0x53");
        }

        JwtEnvelopeClaims claims;
        try
        {
            var payloadJson = Encoding.UTF8.GetString(Crypto.Base64UrlDecode(parts[1]));
            claims = JsonSerializer.Deserialize<JwtEnvelopeClaims>(payloadJson, Json.Options)
                ?? throw new InvalidOperationException("boot-0x54");
        }
        catch
        {
            throw new InvalidOperationException("boot-0x54");
        }

        if (!string.Equals(claims.Issuer, BootstrapperSettings.EnvelopeIssuer, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("boot-0x55");
        }

        if (!string.Equals(claims.Audience, BootstrapperSettings.EnvelopeAudience, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("boot-0x56");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (claims.NotBefore > 0 && now < claims.NotBefore)
        {
            throw new InvalidOperationException("boot-0x57");
        }

        if (claims.Exp > 0 && now >= claims.Exp)
        {
            throw new InvalidOperationException("boot-0x58");
        }

        var envelopeKey = Crypto.DeriveAesKey(BootstrapperSettings.EnvelopeAudience, BootstrapperSettings.EnvelopePepper);
        var catalogJson = Crypto.Decrypt(claims.Blob, claims.Nonce, claims.Tag, envelopeKey);
        return JsonSerializer.Deserialize<CatalogDocument>(catalogJson, Json.Options)
            ?? throw new InvalidOperationException("boot-0x59");
    }
}
