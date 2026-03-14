using Microsoft.Extensions.Logging;
using Npgsql;

namespace DataAssetBackend.Infrastructure.Database;

/// <summary>
/// Ensures the BRD Section 4 minimal schema exists (tables introduced in Flyway V3).
/// </summary>
/// <remarks>
/// This project supports running in environments where migrations might not be executed automatically
/// before the API starts (e.g., preview/dev deployments). When that happens, Section 4 endpoints
/// can fail with Postgres "relation does not exist" errors, which the API maps to a generic 500.
///
/// This bootstrapper performs a minimal, idempotent check/creation of the Section 4 tables using
/// <c>CREATE TABLE IF NOT EXISTS</c>.
///
/// Important constraints:
/// - Only covers the Section 4 tables introduced in V3.
/// - Does not attempt to modify existing columns/constraints (no destructive changes).
/// - Safe to run repeatedly on every startup.
/// </remarks>
public sealed class Section4SchemaBootstrapper
{
    private readonly NpgsqlConnectionFactory _connectionFactory;
    private readonly ILogger<Section4SchemaBootstrapper> _logger;

    public Section4SchemaBootstrapper(NpgsqlConnectionFactory connectionFactory, ILogger<Section4SchemaBootstrapper> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Ensures required Section 4 tables exist in the connected database.
    /// </summary>
    /// <remarks>
    /// Returns normally if the database is not configured (so the API can still boot and /healthz can report it).
    /// Any other exception is logged and rethrown to avoid silently masking unexpected DB permission issues.
    /// </remarks>
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        NpgsqlConnection? conn = null;

        try
        {
            conn = await _connectionFactory.OpenAsync(cancellationToken);

            // Run as a single batch; all statements are idempotent.
            const string ddl = """
                CREATE TABLE IF NOT EXISTS site_profile (
                    site_profile_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    site_id TEXT NOT NULL,
                    created_by TEXT NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    modified_by TEXT NOT NULL,
                    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                    correlation_id TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS wwts_process_stream (
                    wwts_process_stream_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    site_id TEXT NOT NULL,
                    stream_name TEXT NOT NULL,
                    created_by TEXT NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    modified_by TEXT NOT NULL,
                    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                    correlation_id TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS chemical_raw_material (
                    chemical_raw_material_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    site_id TEXT NOT NULL,
                    chemical_name TEXT NOT NULL,
                    created_by TEXT NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    modified_by TEXT NOT NULL,
                    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                    correlation_id TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS chemical_sds (
                    chemical_sds_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    site_id TEXT NOT NULL,
                    chemical_name TEXT NOT NULL,
                    created_by TEXT NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    modified_by TEXT NOT NULL,
                    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                    correlation_id TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS lab_data_configuration (
                    lab_data_configuration_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    site_id TEXT NOT NULL,
                    configuration_name TEXT NOT NULL,
                    created_by TEXT NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    modified_by TEXT NOT NULL,
                    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                    correlation_id TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS water_process_configuration (
                    water_process_configuration_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    site_id TEXT NOT NULL,
                    configuration_name TEXT NOT NULL,
                    created_by TEXT NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    modified_by TEXT NOT NULL,
                    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                    correlation_id TEXT NOT NULL
                );
                """;

            await using var cmd = new NpgsqlCommand(ddl, conn)
            {
                // DDL can sometimes be slow on first run; do not time out prematurely.
                CommandTimeout = 0
            };

            await cmd.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Section4 schema bootstrap: ensured Section4 tables exist.");
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Database is not configured", StringComparison.OrdinalIgnoreCase))
        {
            // Keep startup tolerant; /healthz will report DB status.
            _logger.LogWarning(ex, "Section4 schema bootstrap skipped: database is not configured.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Section4 schema bootstrap failed.");
            throw;
        }
        finally
        {
            if (conn is not null)
            {
                await conn.DisposeAsync();
            }
        }
    }
}
