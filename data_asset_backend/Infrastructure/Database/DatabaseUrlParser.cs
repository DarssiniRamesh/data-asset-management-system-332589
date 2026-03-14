using System.Web;

namespace DataAssetBackend.Infrastructure.Database;

/// <summary>
/// Helper for parsing DATABASE_URL (postgres URL form) into a driver-agnostic connection string.
/// </summary>
internal static class DatabaseUrlParser
{
    /// <summary>
    /// Converts a postgres URL (e.g. postgresql://user:pass@host:5432/db?sslmode=require)
    /// into a simple ADO-style key/value connection string.
    /// </summary>
    /// <remarks>
    /// Contract:
    /// - Inputs: postgres URL string
    /// - Outputs: ADO-style connection string (Host=...;Port=...;Database=...;Username=...;Password=...;Ssl Mode=...;Trust Server Certificate=...)
    /// - Errors: throws <see cref="FormatException"/> for invalid URL formats
    /// - Side effects: none
    /// </remarks>
    public static string ToAdoLikeConnectionString(string databaseUrl)
    {
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            throw new FormatException("DATABASE_URL is empty.");
        }

        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri))
        {
            throw new FormatException("DATABASE_URL is not a valid absolute URI.");
        }

        if (!string.Equals(uri.Scheme, "postgres", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "postgresql", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException($"Unsupported DATABASE_URL scheme '{uri.Scheme}'. Expected postgres/postgresql.");
        }

        var (username, password) = ParseUserInfo(uri.UserInfo);

        var database = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new FormatException("DATABASE_URL missing database name in path.");
        }

        var host = uri.Host;
        var port = uri.IsDefaultPort ? 5432 : uri.Port;

        var sslMode = "Require";
        var trustServerCertificate = "true";

        ApplyQueryParameters(uri, ref sslMode, ref trustServerCertificate);

        // Escape semicolons to avoid breaking key/value format. (Very rare, but safe.)
        static string esc(string s) => s.Replace(";", "\\;", StringComparison.Ordinal);

        return string.Join(';', new[]
        {
            $"Host={esc(host)}",
            $"Port={port}",
            $"Database={esc(database)}",
            $"Username={esc(username)}",
            $"Password={esc(password)}",
            $"Ssl Mode={esc(sslMode)}",
            $"Trust Server Certificate={esc(trustServerCertificate)}"
        }) + ";";
    }

    private static (string Username, string Password) ParseUserInfo(string userInfo)
    {
        if (string.IsNullOrWhiteSpace(userInfo))
        {
            // Allow passwordless URLs (useful for some local setups).
            return (string.Empty, string.Empty);
        }

        var parts = userInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(parts[0]);
        var pass = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        return (user, pass);
    }

    private static void ApplyQueryParameters(Uri uri, ref string sslMode, ref string trustServerCertificate)
    {
        // Support common URL params when present (sslmode, trust_server_certificate).
        var q = HttpUtility.ParseQueryString(uri.Query);

        var sslModeQuery = q.Get("sslmode");
        if (!string.IsNullOrWhiteSpace(sslModeQuery))
        {
            sslMode = sslModeQuery;
        }

        var trust = q.Get("trust_server_certificate");
        if (!string.IsNullOrWhiteSpace(trust))
        {
            trustServerCertificate = trust;
        }
    }
}
