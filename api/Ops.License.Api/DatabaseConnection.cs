using Npgsql;

namespace Ops.License.Api;

internal static class DatabaseConnection
{
    internal static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertUriToNpgsql(raw);
        }

        return raw;
    }

    private static string ConvertUriToNpgsql(string uri)
    {
        var parsed = new Uri(uri);
        var userInfo = parsed.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = parsed.Host,
            Port = parsed.Port > 0 ? parsed.Port : 5432,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            Database = parsed.AbsolutePath.TrimStart('/'),
            SslMode = SslMode.Require
        };

        if (!string.IsNullOrWhiteSpace(parsed.Query))
        {
            foreach (var pair in parsed.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                var key = parts[0];
                var value = Uri.UnescapeDataString(parts[1]);
                if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                {
                    builder.SslMode = value.ToLowerInvariant() switch
                    {
                        "disable" => SslMode.Disable,
                        "prefer" => SslMode.Prefer,
                        "require" => SslMode.Require,
                        "verify-ca" => SslMode.VerifyCA,
                        "verify-full" => SslMode.VerifyFull,
                        _ => SslMode.Require
                    };
                }
            }
        }

        return builder.ConnectionString;
    }
}
