# Data Asset Backend (.NET)

This workspace contains the .NET backend container (`data_asset_backend`).

## Coding standard tooling (analyzers/format)

The backend uses repo-level configuration for consistent formatting and analyzer behavior:

- `.editorconfig` enforces C# formatting and code-style preferences.
- `Directory.Build.props` enables .NET analyzers and enforces code-style rules during build.
- `.config/dotnet-tools.json` pins `dotnet-format` for consistent formatting in CI and locally.

### Restore local tools

```bash
dotnet tool restore
```

### Format code

```bash
dotnet format
```

### Verify formatting (CI-style)

```bash
dotnet format --verify-no-changes
```

## Database migrations (Neon) without Docker

Docker is not available in this environment. To apply the repo’s Flyway-style SQL migrations (including **V2**) to Neon, use the
built-in non-Docker migration runner:

- See: `data_asset_backend/db/README.md` (section “Migrations (no Docker): .NET MigrationRunner”)

Quick run (from `data_asset_backend/`, recommended):

```bash
bash ./run-migrations.sh
```

Copy/paste-safe dry run:

```bash
bash ./run-migrations.sh --dry-run
```

Notes:
- This wrapper exists to prevent accidental `dotnet run` fallback to the web host (which binds to port 3001).
- If you run via `dotnet` directly, keep it on a single line, e.g.:
  `dotnet run --project ./MigrationRunner/MigrationRunner.csproj -- --dry-run`
- The `--` separator is required before any MigrationRunner arguments (and is safe even if you pass none).

## Local run

From `data_asset_backend/`:

```bash
dotnet run
```