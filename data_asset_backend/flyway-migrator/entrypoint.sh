#!/usr/bin/env sh
set -eu

# Contract:
# - Inputs (env vars): FLYWAY_URL, FLYWAY_USER, FLYWAY_PASSWORD
# - Optional: FLYWAY_LOCATIONS, FLYWAY_CONNECT_RETRIES
# - Behavior: run flyway migrate; exit non-zero on failure (API must not start if migrations fail)

if [ -z "${FLYWAY_URL:-}" ]; then
  echo "FLYWAY_URL is required" >&2
  exit 2
fi

if [ -z "${FLYWAY_USER:-}" ]; then
  echo "FLYWAY_USER is required" >&2
  exit 2
fi

if [ -z "${FLYWAY_PASSWORD:-}" ]; then
  echo "FLYWAY_PASSWORD is required" >&2
  exit 2
fi

CONNECT_RETRIES="${FLYWAY_CONNECT_RETRIES:-10}"
LOCATIONS="${FLYWAY_LOCATIONS:-filesystem:/flyway/sql}"

echo "Running Flyway migrations..."
echo "  url: (redacted)"
echo "  user: ${FLYWAY_USER}"
echo "  locations: ${LOCATIONS}"
echo "  connectRetries: ${CONNECT_RETRIES}"

exec flyway \
  -url="${FLYWAY_URL}" \
  -user="${FLYWAY_USER}" \
  -password="${FLYWAY_PASSWORD}" \
  -locations="${LOCATIONS}" \
  -connectRetries="${CONNECT_RETRIES}" \
  migrate
