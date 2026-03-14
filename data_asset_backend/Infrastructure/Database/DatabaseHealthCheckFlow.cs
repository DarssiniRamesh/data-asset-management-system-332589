using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace DataAssetBackend.Infrastructure.Database;

/// <summary>
/// Request object for the database health check flow.
/// </summary>
public sealed record DatabaseHealthCheckRequest;

/// <summary>
/// Response object for the database health check flow.
/// </summary>
public sealed record DatabaseHealthCheckResult(bool IsConfigured, bool IsHealthy, string? Error);

/// <summary>
/// Reusable flow that checks whether Postgres is reachable with the configured connection string.
/// </summary>
public static class DatabaseHealthCheckFlow
{
    // PUBLIC_INTERFACE
    /// <summary>
    /// Runs a DB connectivity health check.
    /// </summary>
    /// <param name="request">Request parameters (currently empty; reserved for future options).</param>
    /// <param name="configProvider">Resolves the DB connection string.</param>
    /// <param name="logger">Logger to record start/end/errors.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="DatabaseHealthCheckResult"/> with configuration and connectivity status.</returns>
    public static async Task<DatabaseHealthCheckResult> RunAsync(
        DatabaseHealthCheckRequest request,
        DatabaseConfigProvider configProvider,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        _ = request;

        logger.LogDebug("DatabaseHealthCheckFlow starting.");

        var resolution = configProvider.TryResolveConnectionString(logger);
        if (!resolution.IsConfigured)
        {
            if (!string.IsNullOrWhiteSpace(resolution.Error))
            {
                logger.LogWarning("DatabaseHealthCheckFlow: DB not configured due to error: {Error}", resolution.Error);
                return new DatabaseHealthCheckResult(IsConfigured: false, IsHealthy: false, Error: resolution.Error);
            }

            logger.LogDebug("DatabaseHealthCheckFlow: DB not configured.");
            return new DatabaseHealthCheckResult(IsConfigured: false, IsHealthy: false, Error: null);
        }

        try
        {
            if (!TryExtractHostPort(resolution.ConnectionString!, out var host, out var port))
            {
                logger.LogWarning("DatabaseHealthCheckFlow: unable to extract host/port from connection string.");
                return new DatabaseHealthCheckResult(IsConfigured: true, IsHealthy: false, Error: "Unable to parse DB host/port from connection string.");
            }

            // TCP-level check only: confirms network reachability (not authentication, SSL, or schema).
            // This keeps the backend build independent of a concrete Postgres driver package.
            using var client = new TcpClient();

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(2));

            await client.ConnectAsync(host, port, connectCts.Token);

            logger.LogDebug("DatabaseHealthCheckFlow completed successfully (TCP connect ok).");
            return new DatabaseHealthCheckResult(IsConfigured: true, IsHealthy: true, Error: null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DatabaseHealthCheckFlow failed to reach Postgres host/port.");
            return new DatabaseHealthCheckResult(IsConfigured: true, IsHealthy: false, Error: "Failed to reach Postgres host/port.");
        }
    }

    private static bool TryExtractHostPort(string connectionString, out string host, out int port)
    {
        // Extremely small, tolerant parser for key/value pairs like:
        // "Host=...;Port=5432;Database=...;Username=...;Password=...;"
        // If Port is missing, default 5432.
        host = string.Empty;
        port = 5432;

        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            var kv = p.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2)
            {
                continue;
            }

            var key = kv[0];
            var value = kv[1];

            if (key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Server", StringComparison.OrdinalIgnoreCase))
            {
                host = value;
            }
            else if (key.Equals("Port", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var parsedPort))
            {
                port = parsedPort;
            }
        }

        return !string.IsNullOrWhiteSpace(host);
    }
}
