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

## Local run

From `data_asset_backend/`:

```bash
dotnet run
```