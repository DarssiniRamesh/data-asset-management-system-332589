using Microsoft.Extensions.Logging;
using Npgsql;

namespace DataAssetBackend.Infrastructure.Database;

/// <summary>
/// Factory for opening Npgsql connections using the application's configured Postgres connection string.
/// </summary>
public sealed class NpgsqlConnectionFactory
{
    private readonly DatabaseConfigProvider _configProvider;
    private readonly ILogger<NpgsqlConnectionFactory> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="NpgsqlConnectionFactory" />.
    /// </summary>
    public NpgsqlConnectionFactory(DatabaseConfigProvider configProvider, ILogger<NpgsqlConnectionFactory> logger)
    {
        _configProvider = configProvider;
        _logger = logger;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Opens and returns an <see cref="NpgsqlConnection"/> using configured connection string resolution rules.
    /// </summary>
    /// <remarks>
    /// Contract:
    /// - Inputs: none (uses <see cref="DatabaseConfigProvider"/> for resolution)
    /// - Outputs: an opened <see cref="NpgsqlConnection"/> (caller owns disposal)
    /// - Errors:
    ///   - throws <see cref="InvalidOperationException"/> if DB is not configured
    ///   - throws <see cref="NpgsqlException"/> / <see cref="TimeoutException"/> for connectivity issues
    /// - Side effects: network connection to Postgres
    /// </remarks>
    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var resolution = _configProvider.TryResolveConnectionString(_logger);
        if (!resolution.IsConfigured || string.IsNullOrWhiteSpace(resolution.ConnectionString))
        {
            var msg = string.IsNullOrWhiteSpace(resolution.Error)
                ? "Database is not configured (missing connection string)."
                : $"Database is not configured due to error: {resolution.Error}";
            throw new InvalidOperationException(msg);
        }

        var conn = new NpgsqlConnection(resolution.ConnectionString);

        try
        {
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Postgres connection.");
            await conn.DisposeAsync();
            throw;
        }
    }
}
