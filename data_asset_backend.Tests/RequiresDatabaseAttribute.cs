using Xunit.Sdk;

namespace DataAssetBackend.Tests;

/// <summary>
/// xUnit v2-compatible attribute that skips tests at runtime (reported as "Skipped")
/// when database-backed integration testing isn't possible in the current environment.
///
/// This is intentionally implemented as a BeforeAfterTestAttribute so it runs before any
/// test method body and (critically) before class fixtures are instantiated, ensuring:
/// - DB-backed tests never execute through the non-DB TestAppFactory path
/// - DB-backed tests do not fail with "Database is not configured" in CI environments
///   without DATABASE_URL and without Docker/Testcontainers.
///
/// Selection rules:
/// - If DATABASE_URL (or database_url) is set -> allow tests to run (managed DB).
/// - Else if Docker is available -> allow tests to run (Testcontainers).
/// - Else -> skip the test with a clear reason.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequiresDatabaseAttribute : BeforeAfterTestAttribute
{
    private const string SkipPrefix = "SKIP:";

    public override void Before(System.Reflection.MethodInfo methodUnderTest)
    {
        // Prefer a managed DB connection if provided.
        var dbUrl =
            NormalizeEnv(Environment.GetEnvironmentVariable("DATABASE_URL")) ??
            NormalizeEnv(Environment.GetEnvironmentVariable("database_url"));

        if (!string.IsNullOrWhiteSpace(dbUrl))
        {
            // DB URL exists; DbTestAppFactory will parse it and fail fast if invalid.
            return;
        }

        if (IsDockerAvailable())
        {
            // Docker available; DbTestAppFactory will use Testcontainers.
            return;
        }

        var reason =
            "Skipping DB-backed tests: DATABASE_URL not set and Docker is not available (cannot start Testcontainers). " +
            "Set DATABASE_URL to a Postgres URL (e.g., Neon) or enable Docker to run DB-backed tests.";

        // xUnit v2 supports runtime skipping when throwing an XunitException
        // prefixed with "SKIP:" (this is the most compatible approach across versions).
        throw new XunitException($"{SkipPrefix} {reason}");
    }

    private static string? NormalizeEnv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Trim whitespace and optional surrounding quotes.
        return value.Trim().Trim('"').Trim('\'');
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            // Minimal, deterministic docker availability check.
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "version --format '{{.Server.Version}}'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

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
}
