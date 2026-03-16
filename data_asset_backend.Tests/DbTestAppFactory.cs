using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DataAssetBackend.Infrastructure.Database;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Configurations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
    private readonly IContainer _postgresContainer;

    private string? _connectionString;
    private string? _accessToken;

    public DbTestAppFactory()
    {
        // Use explicit credentials so the connection string is deterministic.
        var postgresConfig = new PostgreSqlTestcontainerConfiguration
        {
            Database = "assetdb",
            Username = "postgres",
            Password = "postgres"
        };

        _postgresContainer = new TestcontainersBuilder<PostgreSqlTestcontainer>()
            .WithDatabase(postgresConfig)
            .WithImage("postgres:16-alpine")
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

        builder.ConfigureServices(services =>
        {
            // Ensure no test-time stubs override real DB connectivity.
            // (The default TestAppFactory replaces NpgsqlConnectionFactory; this factory must not.)
            services.RemoveAll<NpgsqlConnectionFactory>();

            // Re-add the real factory using a config provider that resolves our injected connection string.
            services.AddSingleton<NpgsqlConnectionFactory>(sp =>
            {
                var configProvider = sp.GetRequiredService<DatabaseConfigProvider>();
                var logger = sp.GetRequiredService<ILogger<NpgsqlConnectionFactory>>();
                return new NpgsqlConnectionFactory(configProvider, logger);
            });

            // Make sure DatabaseConfigProvider sees our connection string.
            // DatabaseConfigProvider prefers ConnectionStrings:Default.
            services.PostConfigure<Microsoft.Extensions.Configuration.ConfigurationOptions>(_ => { });
        });

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
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();

        // Build a connection string for Npgsql.
        var host = _postgresContainer.Hostname;
        var port = _postgresContainer.GetMappedPublicPort(5432);

        _connectionString = $"Host={host};Port={port};Database=assetdb;Username=postgres;Password=postgres;Ssl Mode=Disable;";

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

    public async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
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
