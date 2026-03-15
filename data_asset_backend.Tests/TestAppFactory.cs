using System.Security.Claims;
using System.Text.Encodings.Web;
using DataAssetBackend.Features.Assets;
using DataAssetBackend.Features.Masters;
using DataAssetBackend.Features.Section4;
using DataAssetBackend.Infrastructure.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataAssetBackend.Tests;

/// <summary>
/// Creates an in-memory test host for the DataAssetBackend minimal API.
/// </summary>
public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    // Keep default behavior: the API should start even when DB isn't configured.
    // Tests intentionally avoid assuming database availability.

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Run the app under a dedicated environment so Program.cs can apply
        // test-specific behaviors (e.g., disabling the strict auth fallback policy).
        builder.UseEnvironment("Testing");

        // DB configuration:
        // In Testing, we keep the host DB-independent by default.
        //
        // Why:
        // - CI environments for this kata often do not provide a running Postgres.
        // - For validation-focused tests, we want 400 (validation) responses even when DB is absent.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Intentionally no-op: do not inject a localhost connection string fallback.
            // This prevents "connection refused" hard failures when Postgres is absent.
        });

        // IMPORTANT:
        // Many tests are request-validation tests that expect 400 responses.
        // However, several endpoints explicitly call RequireAuthorization(...), which would
        // return 401 unless we provide an authenticated principal.
        //
        // In Testing only, we install a permissive auth scheme and set it as default so
        // authorized endpoints can be reached and validation runs.
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName,
                    _ => { });

            // -----------------------------------------------------------------
            // DB-independent override:
            // Replace DB-backed repositories with stubs that throw InvalidOperationException
            // when invoked. This keeps the host startable without a DB and ensures request
            // validation (400) is not masked by DB failures (503) for invalid payloads.
            // -----------------------------------------------------------------
            services.RemoveAll<NpgsqlConnectionFactory>();
            services.RemoveAll<AssetRepository>();
            services.RemoveAll<AssetCopyRepository>();
            services.RemoveAll<AssetCopyLineageRepository>();
            services.RemoveAll<MasterRepository>();
            services.RemoveAll<Section4Repository>();

            services.AddSingleton<NpgsqlConnectionFactory>(_ => new ThrowingNpgsqlConnectionFactory());
            services.AddSingleton<AssetRepository>(_ => new ThrowingAssetRepository());
            services.AddSingleton<AssetCopyRepository>(_ => new ThrowingAssetCopyRepository());
            services.AddSingleton<AssetCopyLineageRepository>(_ => new ThrowingAssetCopyLineageRepository());
            services.AddSingleton<MasterRepository>(_ => new ThrowingMasterRepository());
            services.AddSingleton<Section4Repository>(_ => new ThrowingSection4Repository());
        });
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Provide a principal with all roles used by the API policies,
            // so any RequireAuthorization("CanRead"/"CanWrite"/"AdminOnly") passes.
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, "test-user"),
                    new Claim(ClaimTypes.Role, "Admin"),
                    new Claim(ClaimTypes.Role, "Editor"),
                    new Claim(ClaimTypes.Role, "Viewer"),
                },
                authenticationType: SchemeName);

            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    /// <summary>
    /// Throws when any DB access is attempted.
    /// </summary>
    private static InvalidOperationException DbNotConfigured()
        => new("Database is not configured (tests run DB-independent).");

    private sealed class ThrowingNpgsqlConnectionFactory : NpgsqlConnectionFactory
    {
        public ThrowingNpgsqlConnectionFactory()
            : base(
                configProvider: new DatabaseConfigProvider(),
                logger: LoggerFactory.Create(b => b.AddDebug()).CreateLogger<NpgsqlConnectionFactory>())
        {
        }

        public override Task<Npgsql.NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default)
            => throw DbNotConfigured();
    }

    private sealed class ThrowingAssetRepository : AssetRepository
    {
        public ThrowingAssetRepository()
            : base(
                connectionFactory: new ThrowingNpgsqlConnectionFactory(),
                logger: LoggerFactory.Create(b => b.AddDebug()).CreateLogger<AssetRepository>())
        {
        }
    }

    private sealed class ThrowingAssetCopyRepository : AssetCopyRepository
    {
        public ThrowingAssetCopyRepository()
            : base(
                connectionFactory: new ThrowingNpgsqlConnectionFactory(),
                logger: LoggerFactory.Create(b => b.AddDebug()).CreateLogger<AssetCopyRepository>())
        {
        }
    }

    private sealed class ThrowingAssetCopyLineageRepository : AssetCopyLineageRepository
    {
        public ThrowingAssetCopyLineageRepository()
            : base(
                connectionFactory: new ThrowingNpgsqlConnectionFactory(),
                logger: LoggerFactory.Create(b => b.AddDebug()).CreateLogger<AssetCopyLineageRepository>())
        {
        }
    }

    private sealed class ThrowingMasterRepository : MasterRepository
    {
        public ThrowingMasterRepository()
            : base(
                connectionFactory: new ThrowingNpgsqlConnectionFactory(),
                logger: LoggerFactory.Create(b => b.AddDebug()).CreateLogger<MasterRepository>())
        {
        }
    }

    private sealed class ThrowingSection4Repository : Section4Repository
    {
        public ThrowingSection4Repository()
            : base(
                connectionFactory: new ThrowingNpgsqlConnectionFactory(),
                logger: LoggerFactory.Create(b => b.AddDebug()).CreateLogger<Section4Repository>())
        {
        }
    }
}
