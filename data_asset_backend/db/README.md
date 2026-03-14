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

## Flyway migrator (planned runtime)

A containerized migrator is provided at:

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
