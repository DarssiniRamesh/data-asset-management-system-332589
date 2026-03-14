# Database (Neon Postgres) + Flyway migrations

This backend is being prepared to use **Neon Postgres** and **Flyway** for schema migrations, following the repository CodeWiki plan.

## Connection configuration (backend API)

The backend resolves its Postgres connection string using **one** of:

1) `ConnectionStrings__Default` (recommended .NET convention)
2) `DATABASE_URL` (Postgres URL form; common for Neon / PaaS)

No values are committed to the repository.

## Migrations layout

Migrations live in:

- `data_asset_backend/db/migrations/`
  - `V1__init.sql` (baseline)

## Migrations (no Docker): .NET MigrationRunner (recommended in this environment)

Docker is not available in this environment, so this repo provides a **non-Docker migration runner** that applies the existing
Flyway-style SQL scripts under:

- `data_asset_backend/db/migrations/` (`V1__*.sql`, `V2__*.sql`, ...)

### How it works

- Reads DB config from environment:
  1) `DATABASE_URL` (preferred; e.g. `postgresql://user:pass@host:5432/db?sslmode=require`)
  2) `ConnectionStrings__Default` (optional; .NET connection string)

- Applies pending migrations in version order.
- Tracks applied migrations in a history table:
  - `__app_migration_history`

This history table is **separate** from Flyway’s `flyway_schema_history` to avoid checksum/metadata compatibility issues when the
official Flyway CLI is not used.

### Run it

From `data_asset_backend/`:

```bash
dotnet run --project ./MigrationRunner/MigrationRunner.csproj --
```

This runs migrations as a plain console app and is safe to execute while the backend preview is already running (it does not start Kestrel or bind to port 3001).

Dry run (shows pending versions without applying):

```bash
dotnet run --project MigrationRunner -- --dry-run
```

Optional: specify migrations directory explicitly:

```bash
dotnet run --project MigrationRunner -- --migrations ./db/migrations
```

## Flyway migrator (Docker, optional)

If you have Docker available elsewhere, a containerized Flyway migrator is also provided at:

- `data_asset_backend/flyway-migrator/Dockerfile`

It expects environment variables (names only):

- `FLYWAY_URL`
- `FLYWAY_USER`
- `FLYWAY_PASSWORD`
- `FLYWAY_LOCATIONS` (optional, defaults to `filesystem:/flyway/sql`)
- `FLYWAY_CONNECT_RETRIES` (optional, defaults to `10`)

### Example (Docker)

Run migrations (example only; do not commit real secrets):

```bash
docker build -t data-asset-flyway -f flyway-migrator/Dockerfile .
docker run --rm \
  -e FLYWAY_URL="jdbc:postgresql://<host>:5432/<db>?sslmode=require" \
  -e FLYWAY_USER="<user>" \
  -e FLYWAY_PASSWORD="<password>" \
  data-asset-flyway
```

Notes:
- Flyway uses **JDBC URLs** (e.g., `jdbc:postgresql://...`), not `postgresql://...`.
- The API can use `DATABASE_URL=postgresql://...` or `ConnectionStrings__Default=Host=...;...`.
- In production, run migrator as a **pre-start job/init step**; if it fails, the API **must not** start.
