#!/usr/bin/env bash
# Starts Postgres, the API, and the web app together.
# Ctrl-C stops all of them.
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$PWD"

PGPORT=5433
API_PORT=5236
WEB_PORT=5173

cleanup() {
  echo ""
  echo "Shutting down..."
  [[ -n "${API_PID:-}" ]] && kill "$API_PID" 2>/dev/null || true
  [[ -n "${WEB_PID:-}" ]] && kill "$WEB_PID" 2>/dev/null || true
  wait 2>/dev/null || true
}
trap cleanup EXIT INT TERM

if ! pg_isready -h localhost -p "$PGPORT" -q 2>/dev/null; then
  echo "Postgres is not up on $PGPORT. Start it with: brew services start postgresql@18"
  exit 1
fi

if [[ ! -f "$ROOT/src/JobSuites.Api/appsettings.Development.json" ]]; then
  echo "Missing src/JobSuites.Api/appsettings.Development.json"
  echo "Create it from appsettings.Development.json.example with real local secrets."
  exit 1
fi

echo "Applying migrations..."
dotnet ef database update --project src/JobSuites.Api

# --no-launch-profile skips launchSettings.json, so ASPNETCORE_ENVIRONMENT must
# be set explicitly. Without it the API runs as Production, never loads
# appsettings.Development.json, finds no Jwt:Key, and refuses to start.
echo "Starting API on :${API_PORT}..."
(
  cd src/JobSuites.Api
  ASPNETCORE_ENVIRONMENT=Development \
  DOTNET_ENVIRONMENT=Development \
  dotnet run --no-launch-profile --urls "http://localhost:$API_PORT"
) &
API_PID=$!

for _ in $(seq 1 60); do
  curl -sf "http://localhost:$API_PORT/health" >/dev/null 2>&1 && break
  sleep 1
done

if ! curl -sf "http://localhost:$API_PORT/health" >/dev/null 2>&1; then
  echo "API failed to become healthy. Check the output above."
  exit 1
fi

echo "Starting web on :${WEB_PORT}…"
(cd web && npm run dev -- --port "$WEB_PORT") &
WEB_PID=$!

echo ""
echo "  Web  http://localhost:${WEB_PORT}"
echo "  API  http://localhost:${API_PORT}/swagger"
echo ""
echo "Press Ctrl-C to stop both."

wait
