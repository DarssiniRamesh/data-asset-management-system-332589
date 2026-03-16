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

namespace DataAssetBackend.Tests;

/// <summary>
/// DB-backed in-memory test host for DataAssetBackend minimal API.
/// Spins up an ephemeral Postgres container, applies SQL migrations, and configures the API to use it.
///
/// This factory is intentionally separate from <see cref="TestAppFactory"/>:
/// - TestAppFactory stays DB-independent (fast validation tests).
/// - DbTestAppFactory enables BRD lifecycle/copy/uniqueness/lineage assertions that require persistence.
/// </summary>
public sealed class DbTestAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private string? _connectionString;
    private string? _accessToken;

    public DbTestAppFactory()
    {
        // Testcontainers 4.x API: use PostgreSqlContainer from Testcontainers.PostgreSql package.
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("assetdb")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCleanUp(true)
            .WithName($"data-asset-backend-tests-{Guid.NewGuid():N}")
            .Build();
    }

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
        builder.UseEnvironment("TestingDb");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Inject connection string into configuration so DatabaseConfigProvider resolves it.
            // IMPORTANT: Use .NET config key for connection strings.
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString
            };

            config.AddInMemoryCollection(dict);
        });

        builder.ConfigureServices(services =>
        {
            // Ensure no test-time stubs override real DB connectivity.
            // (The default TestAppFactory replaces NpgsqlConnectionFactory; this factory must not.)
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
        await _postgres.StartAsync();

        // With Testcontainers.PostgreSql, the container provides a ready-to-use connection string.
        _connectionString = _postgres.GetConnectionString();

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
        await _postgres.DisposeAsync();
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
}
