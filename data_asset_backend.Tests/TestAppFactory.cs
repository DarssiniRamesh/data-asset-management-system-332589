using System.Security.Claims;
using System.Text.Encodings.Web;
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
            // Replace the DB connection factory with a stub that throws immediately
            // if anything attempts DB access.
            //
            // Why this works:
            // - All repositories depend on NpgsqlConnectionFactory.
            // - Validation tests should return 400 before any DB call is needed.
            // - If a test exercises a code path that *does* reach DB, we want a
            //   deterministic failure rather than a flaky environment-dependent one.
            //
            // NOTE:
            // We do NOT inherit from repositories here because many repositories are sealed.
            // -----------------------------------------------------------------
            services.RemoveAll<NpgsqlConnectionFactory>();
            services.AddSingleton<NpgsqlConnectionFactory, ThrowingNpgsqlConnectionFactory>();
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

    /// <summary>
    /// Npgsql connection factory that always throws, preventing any real DB access in tests.
    /// </summary>
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
}
