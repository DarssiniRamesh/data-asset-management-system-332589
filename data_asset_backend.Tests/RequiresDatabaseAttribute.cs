using System.Diagnostics;
using Xunit;
using Xunit.Sdk;

namespace DataAssetBackend.Tests;

/// <summary>
/// Attribute marking a test (method or class) as requiring a database.
///
/// Why this exists:
/// - DB-backed tests must be SKIPPED (not failed) when the environment cannot provide a DB.
/// - Skipping must happen at *discovery time* so xUnit never instantiates fixtures (e.g., DbTestAppFactory)
///   and the tests are counted as "Skipped" in the xUnit/TRX summary.
///
/// DB availability rules:
/// - If DATABASE_URL (or database_url) is set -> DB is considered available (managed Postgres).
/// - Else if Docker is available -> DB is considered available (Testcontainers will be used).
/// - Else -> tests are skipped with a clear reason.
///
/// Implementation notes:
/// - We use a custom Fact/Theory attribute with a discoverer so the `Skip` property is populated during
///   discovery. This is the most reliable way to ensure fixtures do not construct.
/// - We also allow annotating whole classes by using this attribute as metadata. A helper base class
///   in each DB test file can apply it to all Facts/Tests, but for this project we keep it simple:
///   we add [RequiresDatabase] at class level AND ensure each test method uses [DbFact]/[DbTheory]
///   as appropriate (see updates in DB-backed test files).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequiresDatabaseAttribute : Attribute
{
    internal static DbTestDatabaseAvailability EvaluateAvailability()
    {
        // Prefer a managed DB connection if provided.
        var dbUrl =
            NormalizeEnv(Environment.GetEnvironmentVariable("DATABASE_URL")) ??
            NormalizeEnv(Environment.GetEnvironmentVariable("database_url"));

        if (!string.IsNullOrWhiteSpace(dbUrl))
        {
            return DbTestDatabaseAvailability.Available("DATABASE_URL is set.");
        }

        if (IsDockerAvailable())
        {
            return DbTestDatabaseAvailability.Available("Docker is available (Testcontainers can run).");
        }

        return DbTestDatabaseAvailability.Unavailable(
            "Skipping DB-backed tests: DATABASE_URL not set and Docker is not available (cannot start Testcontainers). " +
            "Set DATABASE_URL to a Postgres URL (e.g., Neon) or enable Docker to run DB-backed tests.");
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

    internal sealed record DbTestDatabaseAvailability(bool IsAvailable, string Reason)
    {
        public static DbTestDatabaseAvailability Available(string reason) => new(true, reason);
        public static DbTestDatabaseAvailability Unavailable(string reason) => new(false, reason);
    }
}

/// <summary>
/// xUnit Fact that is automatically skipped when a DB is not available.
///
/// PUBLIC_INTERFACE
/// </summary>
[XunitTestCaseDiscoverer("DataAssetBackend.Tests.DbFactDiscoverer", "DataAssetBackend.Tests")]
public sealed class DbFactAttribute : FactAttribute
{
}

/// <summary>
/// xUnit Theory that is automatically skipped when a DB is not available.
///
/// PUBLIC_INTERFACE
/// </summary>
[XunitTestCaseDiscoverer("DataAssetBackend.Tests.DbTheoryDiscoverer", "DataAssetBackend.Tests")]
public sealed class DbTheoryAttribute : TheoryAttribute
{
}

/// <summary>
/// Discovery-time skip logic for <see cref="DbFactAttribute"/>.
/// </summary>
public sealed class DbFactDiscoverer : IXunitTestCaseDiscoverer
{
    private readonly IMessageSink _diagnosticMessageSink;

    public DbFactDiscoverer(IMessageSink diagnosticMessageSink)
    {
        _diagnosticMessageSink = diagnosticMessageSink;
    }

    public IEnumerable<IXunitTestCase> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        ITestMethod testMethod,
        IAttributeInfo factAttribute)
    {
        var availability = RequiresDatabaseAttribute.EvaluateAvailability();

        var discoverer = new FactDiscoverer(_diagnosticMessageSink);
        foreach (var tc in discoverer.Discover(discoveryOptions, testMethod, factAttribute))
        {
            if (!availability.IsAvailable)
            {
                // This is what makes it show as Skipped in xUnit/TRX and prevents fixture construction.
                tc.SkipReason = availability.Reason;
            }

            yield return tc;
        }
    }
}

/// <summary>
/// Discovery-time skip logic for <see cref="DbTheoryAttribute"/>.
/// </summary>
public sealed class DbTheoryDiscoverer : IXunitTestCaseDiscoverer
{
    private readonly IMessageSink _diagnosticMessageSink;

    public DbTheoryDiscoverer(IMessageSink diagnosticMessageSink)
    {
        _diagnosticMessageSink = diagnosticMessageSink;
    }

    public IEnumerable<IXunitTestCase> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        ITestMethod testMethod,
        IAttributeInfo theoryAttribute)
    {
        var availability = RequiresDatabaseAttribute.EvaluateAvailability();

        var discoverer = new TheoryDiscoverer(_diagnosticMessageSink);
        foreach (var tc in discoverer.Discover(discoveryOptions, testMethod, theoryAttribute))
        {
            if (!availability.IsAvailable)
            {
                tc.SkipReason = availability.Reason;
            }

            yield return tc;
        }
    }
}
