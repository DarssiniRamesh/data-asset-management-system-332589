using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DataAssetBackend.Infrastructure.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace DataAssetBackend.Tests;

/// <summary>
/// DB-backed in-memory test host for DataAssetBackend minimal API.
///
/// Flow / selection contract:
/// - Prefer DATABASE_URL (Neon / managed Postgres) when present.
/// - Otherwise, if Docker is available, provision Postgres via Testcontainers.
/// - Otherwise, cleanly skip DB-dependent tests with a clear reason.
///
/// This factory is intentionally separate from <see cref="TestAppFactory"/>:
/// - TestAppFactory stays DB-independent (fast validation tests).
/// - DbTestAppFactory enables BRD lifecycle/copy/uniqueness/lineage assertions that require persistence.
/// </summary>
public sealed class DbTestAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string TestingDbEnvironmentName = "TestingDb";

    private PostgreSqlContainer? _postgres;
    private string? _connectionString;
    private string? _accessToken;

    /// <summary>
    /// Returns a client that includes Authorization + correlation header defaults (caller can override).
    /// </summary>
    public HttpClient CreateAuthedClient()
    {
        var client = CreateClient();

        // Ensure auth-required endpoints can be hit with the real JWT auth stack.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        // A stable correlation ID so tests can assert propagation if needed.
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-Id", "test-corr-fixed");

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Run the app under a dedicated environment.
        builder.UseEnvironment(TestingDbEnvironmentName);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // IMPORTANT:
            // The application prefers ConnectionStrings:Default (standard .NET) when provided.
            // We always inject this to ensure the DB path is exercised for DB-backed tests.
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString
            };

            config.AddInMemoryCollection(dict);
        });

        builder.ConfigureServices(services =>
        {
            // Ensure no test-time stubs override real DB connectivity.
            // (TestAppFactory replaces NpgsqlConnectionFactory; this factory must not.)
            services.RemoveAll<NpgsqlConnectionFactory>();

            // Re-add the real factory using the existing DatabaseConfigProvider.
            services.AddSingleton<NpgsqlConnectionFactory>(sp =>
            {
                var configProvider = sp.GetRequiredService<DatabaseConfigProvider>();
                var logger = sp.GetRequiredService<ILogger<NpgsqlConnectionFactory>>();
                return new NpgsqlConnectionFactory(configProvider, logger);
            });
        });
    }

    public async Task InitializeAsync()
    {
        var logger = LoggerFactory
            .Create(b => b.AddConsole())
            .CreateLogger<DbTestAppFactory>();

        // Note: xUnit's SkipException is not a stable public API across versions.
        // This test project targets xUnit 2.7.0 and previously used members that do not exist,
        // which broke compilation. We therefore avoid SkipException entirely.
        //
        // If DB provisioning isn't possible, ProvisionAsync will throw an XunitException with a clear reason.
        DbTestDatabaseResolution resolution = await DbTestDatabaseProvisioning.ProvisionAsync(logger);

        _connectionString = resolution.ConnectionString;
        _postgres = resolution.Container;

        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            // Should be unreachable given the ProvisionAsync contract.
            throw new InvalidOperationException("DB test setup failed: no connection string was produced.");
        }

        logger.LogInformation("DbTestAppFactory: DB provider selected: {Provider}", resolution.Provider);

        // Ensure schema exists for whichever DB provider we chose.
        await ApplyMigrationsAsync(_connectionString);

        // Create a real token using the API's dev login endpoint.
        using var client = CreateClient();
        var resp = await client.PostAsync(
            "/api/auth/login",
            new StringContent(
                JsonSerializer.Serialize(new { username = "db-test-user", role = "Admin" }),
                Encoding.UTF8,
                "application/json"));

        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // Contract of DevLoginResponse: accessToken field exists (see openapi).
        _accessToken = doc.RootElement.GetProperty("accessToken").GetString();
        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            throw new InvalidOperationException("DB test setup failed: /api/auth/login did not return accessToken.");
        }
    }

    public new async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    private static async Task ApplyMigrationsAsync(string connectionString)
    {
        // Apply SQL migrations exactly as the backend expects (Flyway-style files).
        // We run them in order V1..V3 by file name sorting.
        var backendRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data_asset_backend"));
        var migrationsDir = Path.Combine(backendRoot, "db", "migrations");

        if (!Directory.Exists(migrationsDir))
        {
            throw new DirectoryNotFoundException($"Migrations directory not found: {migrationsDir}");
        }

        var files = Directory
            .EnumerateFiles(migrationsDir, "V*__*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException($"No migration files found in: {migrationsDir}");
        }

        await using var conn = new Npgsql.NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        foreach (var f in files)
        {
            var sql = await File.ReadAllTextAsync(f);
            await using var cmd = new Npgsql.NpgsqlCommand(sql, conn)
            {
                CommandTimeout = 0
            };
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Reusable flow that deterministically resolves how DB-backed tests get a Postgres connection string.
    /// </summary>
    private static class DbTestDatabaseProvisioning
    {
        /// <summary>
        /// Attempts to provision a DB for DB-backed tests.
        ///
        /// Contract:
        /// - Inputs: a logger.
        /// - Outputs: a resolution containing a usable connection string.
        /// - Errors: throws <see cref="SkipException"/> when DB tests should be skipped with a clear reason.
        /// - Side effects: may start a Docker container (Testcontainers) when using container provider.
        /// </summary>
        public static async Task<DbTestDatabaseResolution> ProvisionAsync(ILogger logger)
        {
            // 1) Prefer Neon-style DATABASE_URL.
            var dbUrl = GetFirstNonEmpty(
                Environment.GetEnvironmentVariable("DATABASE_URL"),
                Environment.GetEnvironmentVariable("database_url"));

            dbUrl = NormalizeEnvValue(dbUrl);

            if (!string.IsNullOrWhiteSpace(dbUrl))
            {
                try
                {
                    var ado = DatabaseUrlParser.ToAdoLikeConnectionString(dbUrl);
                    logger.LogInformation("DbTestDatabaseProvisioning: using DATABASE_URL (managed Postgres).");
                    return DbTestDatabaseResolution.FromManaged(ado);
                }
                catch (Exception ex)
                {
                    // If DATABASE_URL is present but invalid, fail fast rather than silently falling back.
                    throw new InvalidOperationException("DATABASE_URL is set but could not be parsed into a Postgres connection string.", ex);
                }
            }

            // 2) Fall back to Testcontainers if Docker is available.
            if (!IsDockerAvailable())
            {
                var reason =
                    "Skipping DB-backed tests: DATABASE_URL not set and Docker is not available (cannot start Testcontainers). " +
                    "Set DATABASE_URL to a Neon/managed Postgres URL to run DB-backed tests.";

                ThrowSkipWithReason(reason);
            }

            logger.LogInformation("DbTestDatabaseProvisioning: DATABASE_URL not set; using Testcontainers Postgres.");

            var container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("assetdb")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .WithCleanUp(true)
                .WithName($"data-asset-backend-tests-{Guid.NewGuid():N}")
                .Build();

            await container.StartAsync();

            return DbTestDatabaseResolution.FromTestcontainer(container, container.GetConnectionString());
        }

        private static void ThrowSkipWithReason(string reason)
        {
            // Ensure the reason is visible in CI logs.
            // stderr is captured by `dotnet test` and by most CI systems.
            Console.Error.WriteLine(reason);

            // We deliberately avoid Xunit.Sdk.SkipException here because its constructor/properties
            // are not stable across xUnit versions and previously broke compilation.
            //
            // Throwing XunitException will fail the DB-backed tests when DB is unavailable, but keeps
            // the reason explicit and unblocks compilation/execution in environments where DB is available.
            throw new XunitException(reason);
        }

        private static bool IsDockerAvailable()
        {
            try
            {
                // Deterministic check without requiring Docker SDKs:
                // - if "docker" executable is missing -> not available
                // - if docker fails -> not available
                var psi = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "version --format '{{.Server.Version}}'",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc is null)
                {
                    return false;
                }

                // Hard timeout so tests don't hang.
                if (!proc.WaitForExit(2500))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    return false;
                }

                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
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

    private sealed record DbTestDatabaseResolution(
        string Provider,
        string ConnectionString,
        PostgreSqlContainer? Container)
    {
        public static DbTestDatabaseResolution FromManaged(string connectionString)
            => new("Managed/DATABASE_URL", connectionString, null);

        public static DbTestDatabaseResolution FromTestcontainer(PostgreSqlContainer container, string connectionString)
            => new("Testcontainers", connectionString, container);
    }
}
