#!/usr/bin/env bash
# Regenerates libs/web/api-client/src/internal/schema.d.ts from the Internal BFF's
# OpenAPI document served by Helm.Host in Development.
#
# Idempotent / safe to re-run: brings up docker compose deps if not already running,
# boots Helm.Host in the background, waits for /health, generates the schema, then
# always stops the Host it started (even on failure), leaving compose services running.
#
# Usage: tools/scripts/generate-api-client.sh
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

HOST_URL="http://localhost:5000"
OPENAPI_URL="$HOST_URL/openapi/internal.json"
OUT_FILE="libs/web/api-client/src/internal/schema.d.ts"
HEALTH_TIMEOUT_SECS=60
HOST_LOG="$(mktemp -t helm-host-log.XXXXXX)"
HOST_PID=""

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"

cleanup() {
  if [[ -n "$HOST_PID" ]] && kill -0 "$HOST_PID" 2>/dev/null; then
    echo "Stopping Helm.Host (pid $HOST_PID)..."
    kill "$HOST_PID" 2>/dev/null || true
    wait "$HOST_PID" 2>/dev/null || true
  fi
  rm -f "$HOST_LOG"
}
trap cleanup EXIT

echo "Ensuring docker compose dependencies are up..."
docker compose up -d --wait

echo "Starting Helm.Host..."
ConnectionStrings__Helm="Host=localhost;Database=helm;Username=helm;Password=helm" \
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="$HOST_URL" \
  dotnet run --project apps/api/Helm.Host >"$HOST_LOG" 2>&1 &
HOST_PID=$!

echo "Waiting for $HOST_URL/health (timeout ${HEALTH_TIMEOUT_SECS}s)..."
elapsed=0
until curl -fsS -o /dev/null "$HOST_URL/health" 2>/dev/null; do
  if ! kill -0 "$HOST_PID" 2>/dev/null; then
    echo "Helm.Host exited before becoming healthy. Log:" >&2
    cat "$HOST_LOG" >&2
    exit 1
  fi
  if (( elapsed >= HEALTH_TIMEOUT_SECS )); then
    echo "Timed out waiting for $HOST_URL/health after ${HEALTH_TIMEOUT_SECS}s. Log:" >&2
    cat "$HOST_LOG" >&2
    exit 1
  fi
  sleep 1
  elapsed=$((elapsed + 1))
done
echo "Helm.Host is healthy."

mkdir -p "$(dirname "$OUT_FILE")"
echo "Generating $OUT_FILE from $OPENAPI_URL..."
npx --yes openapi-typescript "$OPENAPI_URL" -o "$OUT_FILE"

echo "Done. Generated $OUT_FILE"
