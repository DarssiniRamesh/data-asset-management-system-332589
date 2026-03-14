using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace DataAssetBackend.MigrationRunner;

/// <summary>
/// Entry point for running SQL migrations (Flyway-style naming) without Docker.
/// </summary>
internal static class Program
{
    // Prefer running from data_asset_backend/ (then migrations are at ./db/migrations).
    // Also support running from data_asset_backend/MigrationRunner/ (then migrations are at ../db/migrations).
    private const string DefaultMigrationsRelativePathFromBackendRoot = "db/migrations";
    private const string DefaultMigrationsRelativePathFromRunnerDir = "../db/migrations";

    public static async Task<int> Main(string[] args)
    {
        // Boundary: parse args + env, then run a single flow.
        LoadDotEnvIfPresent();

        var request = CliParsing.Parse(args);

        try
        {
            var result = await ApplyMigrationsFlow.RunAsync(request, CancellationToken.None);
            Console.WriteLine($"MigrationRunner: SUCCESS. applied={result.AppliedCount}, skipped={result.SkippedCount}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("MigrationRunner: FAILED.");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void LoadDotEnvIfPresent()
    {
        // Keep behavior aligned with the API (data_asset_backend/Program.cs).
        // This runner is often executed locally where secrets are provided via a .env file.
        try
        {
            var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
            if (!File.Exists(envPath))
            {
                envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
                if (!File.Exists(envPath))
                {
                    return;
                }
            }

            foreach (var rawLine in File.ReadAllLines(envPath))
            {
                var line = rawLine.Trim();

                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                {
                    line = line["export ".Length..].TrimStart();
                }

                var idx = line.IndexOf('=');
                if (idx <= 0)
                {
                    continue;
                }

                var key = line[..idx].Trim();
                var value = line[(idx + 1)..].Trim();

                value = value.Trim().Trim('"').Trim('\'');

                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                {
                    continue;
                }

                Environment.SetEnvironmentVariable(key, value);
            }
        }
        catch
        {
            // Never fail due to .env parsing; a missing/invalid DATABASE_URL will be surfaced explicitly later.
        }
    }

    private static class CliParsing
    {
        // PUBLIC_INTERFACE
        /// <summary>
        /// Parses CLI arguments into a strongly-typed request object.
        /// </summary>
        /// <remarks>
        /// Contract:
        /// - Inputs: args supports:
        ///   --migrations &lt;path&gt;  (default: ../db/migrations relative to MigrationRunner/)
        ///   --dry-run              (prints pending migrations without applying)
        /// - Outputs: <see cref="ApplyMigrationsRequest"/> with normalized absolute paths.
        /// - Errors: throws <see cref="ArgumentException"/> for invalid arguments.
        /// </remarks>
        public static ApplyMigrationsRequest Parse(string[] args)
        {
            string? migrationsPath = null;
            var dryRun = false;

            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];

                if (string.Equals(a, "--migrations", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException("--migrations requires a path value.");
                    }

                    migrationsPath = args[++i];
                }
                else if (string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase))
                {
                    dryRun = true;
                }
                else if (string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase))
                {
                    PrintHelp();
                    Environment.Exit(0);
                }
                else
                {
                    throw new ArgumentException($"Unknown argument: {a}");
                }
            }

            var migrationsDir = ResolveMigrationsPath(migrationsPath);

            return new ApplyMigrationsRequest(
                DatabaseUrl: NormalizeEnvValue(Environment.GetEnvironmentVariable("DATABASE_URL")),
                ConnectionString: NormalizeEnvValue(Environment.GetEnvironmentVariable("ConnectionStrings__Default")),
                MigrationsDirectory: migrationsDir,
                DryRun: dryRun);
        }

        private static DirectoryInfo ResolveMigrationsPath(string? input)
        {
            var baseDir = Directory.GetCurrentDirectory();

            string candidate;
            if (!string.IsNullOrWhiteSpace(input))
            {
                candidate = Path.GetFullPath(input);
            }
            else
            {
                // Try the most common invocation first: run from data_asset_backend/
                var fromBackendRoot = Path.GetFullPath(Path.Combine(baseDir, DefaultMigrationsRelativePathFromBackendRoot));
                if (Directory.Exists(fromBackendRoot))
                {
                    candidate = fromBackendRoot;
                }
                else
                {
                    // Fallback: run from data_asset_backend/MigrationRunner/
                    candidate = Path.GetFullPath(Path.Combine(baseDir, DefaultMigrationsRelativePathFromRunnerDir));
                }
            }

            var di = new DirectoryInfo(candidate);
            if (!di.Exists)
            {
                throw new ArgumentException(
                    $"Migrations directory not found: {di.FullName}. " +
                    "Run from data_asset_backend/ or pass --migrations <path>.");
            }

            return di;
        }

        private static void PrintHelp()
        {
            Console.WriteLine(
                """
                DataAssetBackend MigrationRunner (no Docker)

                Applies Flyway-style versioned SQL migrations (V1__*.sql, V2__*.sql, ...) to Postgres.

                Usage:
                  dotnet run --project MigrationRunner -- [--migrations <path>] [--dry-run]

                Config (env):
                  DATABASE_URL               (preferred; postgresql://user:pass@host:5432/db?sslmode=require)
                  ConnectionStrings__Default (optional; .NET-style connection string)

                Notes:
                  - Uses a repo-local history table: __app_migration_history (separate from Flyway's flyway_schema_history).
                  - Designed for environments where Docker is unavailable.
                """);
        }

        private static string? NormalizeEnvValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return value.Trim().Trim('"').Trim('\'');
        }
    }
}

internal sealed record ApplyMigrationsRequest(
    string? DatabaseUrl,
    string? ConnectionString,
    DirectoryInfo MigrationsDirectory,
    bool DryRun);

internal sealed record ApplyMigrationsResult(int AppliedCount, int SkippedCount);

internal static class MigrationRunnerConstants
{
    public const string HistoryTableName = "__app_migration_history";
}

internal static class ApplyMigrationsFlow
{
    // PUBLIC_INTERFACE
    /// <summary>
    /// Applies pending migrations in version order, tracking applied versions in a DB history table.
    /// </summary>
    /// <remarks>
    /// Contract:
    /// - Inputs:
    ///   - Connection info: DATABASE_URL (preferred) or ConnectionStrings__Default
    ///   - Migrations directory containing Flyway-style versioned scripts: V{N}__description.sql
    /// - Outputs: counts of applied and skipped migrations.
    /// - Errors:
    ///   - throws <see cref="InvalidOperationException"/> if DB config missing
    ///   - throws <see cref="NpgsqlException"/> for DB connectivity/DDL/DML failures
    ///   - throws <see cref="IOException"/> / <see cref="UnauthorizedAccessException"/> for file issues
    /// - Side effects:
    ///   - creates history table if missing
    ///   - executes SQL from pending scripts (each script is executed in a transaction)
    ///   - inserts history rows after successful script execution
    /// </remarks>
    public static async Task<ApplyMigrationsResult> RunAsync(ApplyMigrationsRequest request, CancellationToken cancellationToken)
    {
        Console.WriteLine("ApplyMigrationsFlow: starting");
        Console.WriteLine($"  migrationsDir: {request.MigrationsDirectory.FullName}");
        Console.WriteLine($"  dryRun: {request.DryRun}");

        var connectionString = ResolveConnectionString(request);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await EnsureHistoryTableAsync(conn, cancellationToken);

        var migrations = MigrationDiscovery.Discover(request.MigrationsDirectory);

        var applied = await MigrationHistory.GetAppliedVersionsAsync(conn, cancellationToken);

        var pending = migrations
            .Where(m => !applied.Contains(m.Version))
            .ToList();

        Console.WriteLine($"ApplyMigrationsFlow: discovered={migrations.Count}, alreadyApplied={applied.Count}, pending={pending.Count}");

        if (pending.Count == 0)
        {
            Console.WriteLine("ApplyMigrationsFlow: nothing to do.");
            return new ApplyMigrationsResult(AppliedCount: 0, SkippedCount: migrations.Count);
        }

        if (request.DryRun)
        {
            Console.WriteLine("ApplyMigrationsFlow: DRY RUN - pending migrations:");
            foreach (var m in pending)
            {
                Console.WriteLine($"  - {m.Version}  {m.File.Name}");
            }

            return new ApplyMigrationsResult(AppliedCount: 0, SkippedCount: migrations.Count - pending.Count);
        }

        var appliedCount = 0;
        foreach (var m in pending)
        {
            Console.WriteLine($"ApplyMigrationsFlow: applying version={m.Version} file={m.File.FullName}");

            var sw = Stopwatch.StartNew();

            var sql = await File.ReadAllTextAsync(m.File.FullName, cancellationToken);

            // Each migration is run in its own transaction: easy to reason about and aligns with typical migration runners.
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);

            try
            {
                await ExecuteSqlAsync(conn, tx, sql, cancellationToken);

                sw.Stop();

                var checksum = MigrationChecksum.ComputeSha256Hex(sql);
                await MigrationHistory.InsertAppliedAsync(conn, tx, new AppliedMigrationRow(
                    Version: m.Version,
                    Description: m.Description,
                    ScriptName: m.File.Name,
                    ChecksumSha256Hex: checksum,
                    ExecutionTimeMs: (int)sw.ElapsedMilliseconds,
                    AppliedAtUtc: DateTimeOffset.UtcNow), cancellationToken);

                await tx.CommitAsync(cancellationToken);

                appliedCount++;
                Console.WriteLine($"ApplyMigrationsFlow: applied version={m.Version} in {sw.ElapsedMilliseconds}ms");
            }
            catch
            {
                try
                {
                    await tx.RollbackAsync(cancellationToken);
                }
                catch
                {
                    // Best-effort rollback only.
                }

                Console.Error.WriteLine($"ApplyMigrationsFlow: FAILED applying version={m.Version} file={m.File.Name}");
                throw;
            }
        }

        Console.WriteLine("ApplyMigrationsFlow: completed successfully");
        return new ApplyMigrationsResult(AppliedCount: appliedCount, SkippedCount: migrations.Count - appliedCount);
    }

    private static string ResolveConnectionString(ApplyMigrationsRequest request)
    {
        // Preferred: ConnectionStrings__Default (ADO-style). Alternative: DATABASE_URL (postgres URL form).
        if (!string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            // Assume it's already a valid Npgsql connection string.
            Console.WriteLine("ApplyMigrationsFlow: using ConnectionStrings__Default (redacted).");
            return request.ConnectionString!;
        }

        if (!string.IsNullOrWhiteSpace(request.DatabaseUrl))
        {
            Console.WriteLine("ApplyMigrationsFlow: using DATABASE_URL (redacted).");
            return DatabaseUrlParser.ToAdoLikeConnectionString(request.DatabaseUrl!);
        }

        throw new InvalidOperationException(
            "Database is not configured. Set DATABASE_URL (preferred) or ConnectionStrings__Default before running migrations.");
    }

    private static async Task EnsureHistoryTableAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        // Separate from Flyway's flyway_schema_history to avoid checksum/metadata compatibility problems.
        // This table is dedicated to this repo's non-Docker runner.
        var ddl = $"""
            CREATE TABLE IF NOT EXISTS {MigrationRunnerConstants.HistoryTableName} (
                id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                version TEXT NOT NULL UNIQUE,
                description TEXT NOT NULL,
                script_name TEXT NOT NULL,
                checksum_sha256_hex TEXT NOT NULL,
                execution_time_ms INT NOT NULL,
                applied_at_utc TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """;

        await using var cmd = new NpgsqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteSqlAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken cancellationToken)
    {
        // Flyway SQL files often contain multiple statements; Npgsql can execute them in a single batch.
        await using var cmd = new NpgsqlCommand(sql, conn, tx)
        {
            CommandTimeout = 0 // migrations may take longer; don't time out unless the server/network does.
        };
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static class MigrationChecksum
    {
        public static string ComputeSha256Hex(string contents)
        {
            var bytes = Encoding.UTF8.GetBytes(contents);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}

internal sealed record MigrationFile(
    string Version,
    string Description,
    FileInfo File);

internal static class MigrationDiscovery
{
    // Flyway-style: V{version}__{description}.sql
    private static readonly string[] AllowedExtensions = [".sql"];

    public static IReadOnlyList<MigrationFile> Discover(DirectoryInfo migrationsDirectory)
    {
        var files = migrationsDirectory
            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(f => AllowedExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
            .Select(TryParse)
            .Where(m => m is not null)
            .Cast<MigrationFile>()
            .OrderBy(m => VersionOrdering.Normalize(m.Version), StringComparer.Ordinal)
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException($"No Flyway-style migrations found in: {migrationsDirectory.FullName}");
        }

        return files;
    }

    private static MigrationFile? TryParse(FileInfo f)
    {
        // Example: V2__brd_asset_configuration_schema.sql
        var name = Path.GetFileNameWithoutExtension(f.Name);

        if (!name.StartsWith('V'))
        {
            return null;
        }

        var parts = name.Split(new[] { "__" }, 2, StringSplitOptions.None);
        if (parts.Length != 2)
        {
            return null;
        }

        var version = parts[0][1..]; // strip 'V'
        var description = parts[1].Replace('_', ' ');

        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        return new MigrationFile(version.Trim(), description.Trim(), f);
    }

    private static class VersionOrdering
    {
        /// <summary>
        /// Produces a sorting key that makes versions like 2, 10, 2.1 order correctly.
        /// </summary>
        public static string Normalize(string version)
        {
            // Split by '.' and pad numeric components to fixed width.
            // This keeps it simple without bringing in semver packages.
            var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                return version;
            }

            var normalized = parts
                .Select(p => int.TryParse(p, out var n) ? n.ToString("D10") : p)
                .ToArray();

            return string.Join(".", normalized);
        }
    }
}

internal sealed record AppliedMigrationRow(
    string Version,
    string Description,
    string ScriptName,
    string ChecksumSha256Hex,
    int ExecutionTimeMs,
    DateTimeOffset AppliedAtUtc);

internal static class MigrationHistory
{
    public static async Task<HashSet<string>> GetAppliedVersionsAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        var applied = new HashSet<string>(StringComparer.Ordinal);

        var sql = $"SELECT version FROM {MigrationRunnerConstants.HistoryTableName};";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            applied.Add(reader.GetString(0));
        }

        return applied;
    }

    public static async Task InsertAppliedAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        AppliedMigrationRow row,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            INSERT INTO {MigrationRunnerConstants.HistoryTableName} (
                version,
                description,
                script_name,
                checksum_sha256_hex,
                execution_time_ms,
                applied_at_utc
            )
            VALUES (
                @version,
                @description,
                @script_name,
                @checksum,
                @execution_time_ms,
                @applied_at_utc
            );
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);

        cmd.Parameters.AddWithValue("version", row.Version);
        cmd.Parameters.AddWithValue("description", row.Description);
        cmd.Parameters.AddWithValue("script_name", row.ScriptName);
        cmd.Parameters.AddWithValue("checksum", row.ChecksumSha256Hex);
        cmd.Parameters.AddWithValue("execution_time_ms", row.ExecutionTimeMs);
        cmd.Parameters.AddWithValue("applied_at_utc", row.AppliedAtUtc.UtcDateTime);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>
/// Helper for parsing DATABASE_URL (postgres URL form) into an Npgsql-friendly connection string.
/// </summary>
internal static class DatabaseUrlParser
{
    public static string ToAdoLikeConnectionString(string databaseUrl)
    {
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            throw new FormatException("DATABASE_URL is empty.");
        }

        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri))
        {
            throw new FormatException("DATABASE_URL is not a valid absolute URI.");
        }

        if (!string.Equals(uri.Scheme, "postgres", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "postgresql", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException($"Unsupported DATABASE_URL scheme '{uri.Scheme}'. Expected postgres/postgresql.");
        }

        var (username, password) = ParseUserInfo(uri.UserInfo);

        var database = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new FormatException("DATABASE_URL missing database name in path.");
        }

        var host = uri.Host;
        var port = uri.IsDefaultPort ? 5432 : uri.Port;

        // Default to Require (Neon commonly needs SSL).
        var sslMode = "Require";
        var trustServerCertificate = "true";

        ApplyQueryParameters(uri, ref sslMode, ref trustServerCertificate);

        static string esc(string s) => s.Replace(";", "\\;", StringComparison.Ordinal);

        // Npgsql supports both "Ssl Mode" and "Trust Server Certificate".
        // Use string separator (not char) to support older target frameworks that don't have string.Join(char, ...).
        return string.Join(";", new[]
        {
            $"Host={esc(host)}",
            $"Port={port}",
            $"Database={esc(database)}",
            $"Username={esc(username)}",
            $"Password={esc(password)}",
            $"Ssl Mode={esc(sslMode)}",
            $"Trust Server Certificate={esc(trustServerCertificate)}"
        }) + ";";
    }

    private static (string Username, string Password) ParseUserInfo(string userInfo)
    {
        if (string.IsNullOrWhiteSpace(userInfo))
        {
            return (string.Empty, string.Empty);
        }

        var parts = userInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(parts[0]);
        var pass = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        return (user, pass);
    }

    private static void ApplyQueryParameters(Uri uri, ref string sslMode, ref string trustServerCertificate)
    {
        // Minimal query parsing without extra dependencies.
        // Supports: ?sslmode=require&trust_server_certificate=true
        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(kv[0]);
            var value = Uri.UnescapeDataString(kv[1]);

            if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
            {
                sslMode = value;
            }
            else if (key.Equals("trust_server_certificate", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
            {
                trustServerCertificate = value;
            }
        }
    }
}
