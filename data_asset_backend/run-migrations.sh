#!/usr/bin/env bash
set -euo pipefail

# run-migrations.sh
#
# Purpose:
#   Execute the non-Docker .NET MigrationRunner reliably (without accidentally starting the web host).
#
# Why this exists:
#   If `dotnet run` is invoked without a correctly-formed `--project <path-to-csproj>`, it can default to the
#   web project and start Kestrel (binding to port 3001). This script makes the intended invocation explicit
#   and forwards any args to the MigrationRunner after `--`.
#
# Usage:
#   From data_asset_backend/:
#     bash ./run-migrations.sh
#     bash ./run-migrations.sh --dry-run
#     bash ./run-migrations.sh --migrations ./db/migrations
#
# Config (env):
#   - DATABASE_URL (preferred) OR ConnectionStrings__Default

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_PATH="${SCRIPT_DIR}/MigrationRunner/MigrationRunner.csproj"

if [[ ! -f "${PROJECT_PATH}" ]]; then
  echo "ERROR: MigrationRunner project not found at: ${PROJECT_PATH}" >&2
  exit 2
fi

# Ensure we execute from the backend root so relative default migrations path resolution is stable.
cd "${SCRIPT_DIR}"

# IMPORTANT:
# - Use an explicit csproj path so dotnet cannot select the web project implicitly.
# - Use `--` so all remaining arguments are passed to MigrationRunner (not dotnet).
exec dotnet run --project "${PROJECT_PATH}" -- "$@"
