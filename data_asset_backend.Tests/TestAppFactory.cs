using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        // Ensure DB-dependent endpoints can run during tests.
        //
        // The app resolves connection strings via DatabaseConfigProvider which prefers:
        //   - ConnectionStrings:Default (aka env var ConnectionStrings__Default)
        //   - DATABASE_URL
        //
        // Several tests expect validation-driven 400 responses from endpoints that also
        // touch the DB (e.g., CreateAsset flow validates parent pseudo asset existence).
        // Without DB config, those endpoints return 503 "Database not configured".
        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Prefer a CI/local-provided env var. The orchestrator/CI can set this.
            // NOTE: Do not hardcode real secrets here.
            var cs = Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING");

            // Fallback for local dev runs: assumes a local postgres is available.
            // If not available, tests may still fail due to connectivity, but they will
            // no longer fail due to *missing* DB configuration.
            cs ??= "Host=localhost;Port=5432;Database=data_asset_backend_test;Username=postgres;Password=postgres";

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = cs
            });
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
}
