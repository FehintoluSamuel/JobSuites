#!/usr/bin/env bash
# Runs one scheduled ingest poll and records the outcome.
#
# This is the body the scheduler calls. It exists as a separate script from the
# launchd plist so the same run can be triggered by hand, which is how you
# verify the schedule is working without waiting for it.
#
#   scripts/ingest-scheduled.sh              # a real poll against the local API
#   scripts/ingest-scheduled.sh --dry-run   # probe and crawl, publish nothing
#   scripts/ingest-scheduled.sh --sweep      # board-wide, ignoring target roles
#
# What gets crawled is not decided here. The publisher asks the API which user
# targets are due, so a user editing their target roles changes tomorrow's crawl
# without touching this script or the schedule. The two knobs below only bound
# how much of that work one run does:
#
#   INGEST_TARGETS   how many target roles to serve this run (default 8)
#   INGEST_LIMIT     landing pages to open per target  (default 25)
#
# It is safe to run while another ingest is in flight: the API holds a
# per-source advisory lock and the second run exits without writing.
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$PWD"

LOG_DIR="$ROOT/logs"
LOG_FILE="${INGEST_LOG:-$LOG_DIR/ingest.log}"
API="${JOBSUITES_API:-http://localhost:5236}"

mkdir -p "$LOG_DIR"

log() { printf '%s %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }

# stdout and stderr both go to the log, and a copy stays on the terminal when a
# human ran this directly. Under launchd stdout is already redirected, so the
# tee would duplicate every line into the same file.
if [[ -t 1 ]]; then
  exec > >(tee -a "$LOG_FILE") 2>&1
else
  exec >>"$LOG_FILE" 2>&1
fi

# The adapter and publisher are standard library only, so a venv is optional
# here. The repo venv is used when it exists so a future dependency lands in
# both paths; a bare python3 is enough today, and requiring a venv that the
# ingest does not need would stop the schedule for no reason.
if [[ -x "$ROOT/.venv/bin/python" ]]; then
  PYTHON="$ROOT/.venv/bin/python"
elif command -v python3 >/dev/null 2>&1; then
  PYTHON="$(command -v python3)"
else
  log "FATAL: no python3 on PATH. The ingest is pure standard library, so any Python 3.11+ will do."
  exit 1
fi

# The API authenticates ingest with Ingest:Key. An environment variable wins, so
# a deployment can inject it without the key ever being written to a file; the
# API's own config is the fallback so there is one source of truth; and the
# development default exists only because the API falls back to it in
# Development. The key is never echoed — it lands in the log's parent process
# environment either way, and must not end up in a log line.
if [[ -z "${INGEST_KEY:-}" ]]; then
  INGEST_KEY="$("$PYTHON" - <<'PY' 2>/dev/null || true
import json, pathlib
cfg = pathlib.Path("src/JobSuites.Api/appsettings.Development.json")
key = ""
if cfg.exists():
    try:
        key = json.loads(cfg.read_text()).get("Ingest", {}).get("Key", "") or ""
    except Exception:
        key = ""
print(key)
PY
)"
fi

if [[ -z "${INGEST_KEY:-}" ]]; then
  # Matches the API's own development fallback in Program.cs. If that default is
  # ever removed, this fails loudly here instead of 401-ing silently at 4pm.
  INGEST_KEY="dev-insecure-ingest-key-do-not-use-outside-development"
  log "WARN: Ingest:Key not found in env or appsettings; using the API's development default."
fi

if ! curl -sf "$API/health" >/dev/null 2>&1; then
  # Reported as a failure rather than skipped: an API that is not running means
  # the schedule is not doing its job, and the dashboard should say so.
  log "ERROR: API not healthy at $API. Is the dev stack running? (scripts/dev.sh)"
  log "The run is being reported so the dashboard can show the gap."
  # --limit 0 with no targets: probe, find nothing to do, and record the
  # failure. It must not crawl, since the API it would report to is not up.
  JOBSUITES_API="$API" INGEST_KEY="$INGEST_KEY" "$PYTHON" -m ingest.publish \
    --api "$API" --ingest-key "$INGEST_KEY" --limit 0 --targets 1 \
    >/dev/null 2>&1 || true
  exit 1
fi

ARGS=(--api "$API" --ingest-key "$INGEST_KEY")
case "${1:-}" in
  --dry-run)
    log "DRY RUN: probing and crawling for due targets, publishing nothing."
    ARGS+=(--dry-run --targets "${INGEST_TARGETS:-8}" --limit "${INGEST_LIMIT:-25}")
    ;;
  --sweep)
    # The board-wide path, for when no profile lists a target role yet. It
    # publishes roles nobody asked for, so it is opt-in rather than the default.
    log "SWEEP: board-wide crawl, ignoring target roles."
    ARGS+=(--sweep --limit "${INGEST_LIMIT:-25}")
    ;;
  "")
    ARGS+=(--targets "${INGEST_TARGETS:-8}" --limit "${INGEST_LIMIT:-25}")
    ;;
  *)
    log "unknown argument: $1 (expected --dry-run or --sweep)"
    exit 2
    ;;
esac

log "starting ingest poll against $API"
if "$PYTHON" -m ingest.publish "${ARGS[@]}"; then
  log "ingest poll finished."
  exit 0
else
  status=$?
  log "ingest poll failed (exit $status). The run is recorded on the dashboard."
  exit "$status"
fi
