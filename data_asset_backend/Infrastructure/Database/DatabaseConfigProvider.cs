using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAssetBackend.Infrastructure.Database;

/// <summary>
/// Resolves the application's Postgres connection string from configuration/environment.
/// </summary>
public sealed class DatabaseConfigProvider
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of <see cref="DatabaseConfigProvider" />.
    /// </summary>
    public DatabaseConfigProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Attempts to resolve a normalized Postgres connection string.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>
    /// A <see cref="DatabaseConnectionResolutionResult"/> describing whether a connection string was resolved and any parsing errors.
    /// </returns>
    public DatabaseConnectionResolutionResult TryResolveConnectionString(ILogger logger)
    {
        // Preferred: .NET standard connection string binding (works well for local/appsettings and env var ConnectionStrings__Default)
        var cs = _configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(cs))
        {
            // We keep it as-is; parsing/validation is handled by the health check flow (which is tolerant).
            return DatabaseConnectionResolutionResult.Configured(cs);
        }

        // Alternative: DATABASE_URL (common in PaaS environments; and explicitly listed in CodeWiki plan)
        // Note: Some environments may not flow env vars into IConfiguration as expected (or may mount them via .env).
        // We therefore check both IConfiguration and Environment directly, and trim common quoting.
        var databaseUrl = GetFirstNonEmpty(
            _configuration["DATABASE_URL"],
            _configuration["DatabaseUrl"],
            Environment.GetEnvironmentVariable("DATABASE_URL"),
            Environment.GetEnvironmentVariable("DatabaseUrl"),
            Environment.GetEnvironmentVariable("database_url"));

        databaseUrl = NormalizeEnvValue(databaseUrl);

        if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            try
            {
                // Convert URL form to an ADO-style key/value string which most Postgres drivers accept.
                // (We intentionally avoid adding a concrete driver dependency in this repo at this time.)
                var normalized = DatabaseUrlParser.ToAdoLikeConnectionString(databaseUrl);
                return DatabaseConnectionResolutionResult.Configured(normalized);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Invalid DATABASE_URL format.");
                return DatabaseConnectionResolutionResult.FromError("Invalid DATABASE_URL (unable to parse).");
            }
        }

        return DatabaseConnectionResolutionResult.NotConfigured();
    }

    private static string? GetFirstNonEmpty(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c))
            {
                return c;
            }
        }

        return null;
    }

    private static string? NormalizeEnvValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        // Trim whitespace and optional surrounding quotes that sometimes appear in env/.env injection.
        return value.Trim().Trim('"').Trim('\'');
    }
}

/// <summary>
/// Result of resolving database connectivity configuration.
/// </summary>
/// <param name="IsConfigured">True when a connection string could be resolved.</param>
/// <param name="ConnectionString">Normalized connection string when configured.</param>
/// <param name="Error">Optional error message (e.g., parsing failure) when not configured.</param>
public sealed record DatabaseConnectionResolutionResult(bool IsConfigured, string? ConnectionString, string? Error)
{
    public static DatabaseConnectionResolutionResult Configured(string connectionString) => new(true, connectionString, null);

    public static DatabaseConnectionResolutionResult NotConfigured() => new(false, null, null);

    public static DatabaseConnectionResolutionResult FromError(string error) => new(false, null, error);
}
