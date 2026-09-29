#!/usr/bin/env bash
# Installs the daily ingest schedule as a launchd agent.
#
#   scripts/install-schedule.sh              # daily at 16:00
#   INGEST_HOUR=9 INGEST_MINUTE=30 scripts/install-schedule.sh
#   scripts/install-schedule.sh --every 3600 # every hour, for testing
#
# Why launchd and not an ASP.NET BackgroundService: docs/ARCHITECTURE.md §9 says
# queued work that is lost on shutdown is work we do not schedule. An in-process
# timer stops whenever the API is not running, which is exactly when a laptop is
# closed — and a missed poll is a silently stale board.
#
# The API is started separately (scripts/dev.sh); this job only talks to it.
# If the API is down the run is still reported, so the dashboard shows why the
# board is stale instead of looking merely quiet.
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$PWD"

LABEL="com.jobsuites.ingest"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
RUNNER="$ROOT/scripts/ingest-scheduled.sh"
LOG_DIR="$ROOT/logs"
HOUR="${INGEST_HOUR:-16}"
MINUTE="${INGEST_MINUTE:-0}"

EVERY=""
if [[ "${1:-}" == "--every" ]]; then
  EVERY="${2:-3600}"
fi

if [[ ! -d "$HOME/Library/LaunchAgents" ]]; then
  mkdir -p "$HOME/Library/LaunchAgents"
fi
mkdir -p "$LOG_DIR"

if [[ ! -x "$RUNNER" ]]; then
  echo "Make the runner executable first: chmod +x scripts/ingest-scheduled.sh"
  exit 1
fi

# launchd inherits a minimal PATH that does not include Homebrew, so curl and
# git would be missing from a run that works perfectly in a terminal.
PATH_EXPORT="/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
if [[ -d "$HOME/.dotnet/tools" ]]; then
  PATH_EXPORT="$PATH_EXPORT:$HOME/.dotnet/tools"
fi

# LoadTrigger is a real problem with StartCalendarInterval on a laptop that is
# asleep at 16:00: the job fires when the machine next wakes. RunAtLoad is
# therefore on, so a missed poll runs at next login rather than being skipped
# until tomorrow.
if [[ -n "$EVERY" ]]; then
  TRIGGER_KEY="StartInterval"
  TRIGGER_BODY="    <integer>$EVERY</integer>"
  WHEN="every ${EVERY}s"
else
  TRIGGER_KEY="StartCalendarInterval"
  TRIGGER_BODY="    <dict>
      <key>Hour</key>
      <integer>$HOUR</integer>
      <key>Minute</key>
      <integer>$MINUTE</integer>
    </dict>"
  printf -v WHEN '%02d:%02d local' "$HOUR" "$MINUTE"
fi

cat > "$PLIST" <<PLIST_EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>$LABEL</string>

  <key>ProgramArguments</key>
  <array>
    <string>/bin/bash</string>
    <string>$RUNNER</string>
  </array>

  <key>WorkingDirectory</key>
  <string>$ROOT</string>

  <key>EnvironmentVariables</key>
  <dict>
    <key>PATH</key>
    <string>$PATH_EXPORT</string>
  </dict>

  <key>$TRIGGER_KEY</key>
$TRIGGER_BODY
  <key>RunAtLoad</key>
  <true/>

  <key>StandardOutPath</key>
  <string>$LOG_DIR/ingest-scheduled.log</string>
  <key>StandardErrorPath</key>
  <string>$LOG_DIR/ingest-scheduled.err.log</string>

  <key>ProcessType</key>
  <string>Background</string>
</dict>
</plist>
PLIST_EOF

# A stale copy of the same label is unloaded first: loading over an existing
# job leaves the old definition in place and the new one ignored, which is a
# confusing way to discover that the schedule did not change.
launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
launchctl bootstrap "gui/$(id -u)" "$PLIST"
launchctl enable "gui/$(id -u)/$LABEL" 2>/dev/null || true

echo "Installed $LABEL"
echo "  runs:     $WHEN"
echo "  runner:   $RUNNER"
echo "  logs:     $LOG_DIR/ingest.log"
echo "           $LOG_DIR/ingest-scheduled.err.log"
echo ""
echo "Run it now without waiting:"
echo "  launchctl kickstart -k gui/$(id -u)/$LABEL"
echo "  tail -f $LOG_DIR/ingest.log"
echo ""
echo "Uninstall with: scripts/uninstall-schedule.sh"

# macOS protects ~/Desktop, ~/Documents and ~/Downloads. A job started by
# launchd has no Terminal grant for those, so it exits 126 with "Operation not
# permitted" no matter how correct the plist is. Caught here because the failure
# is otherwise invisible: the job is loaded, the schedule looks right, and the
# board quietly stops updating.
if [[ "$ROOT" == "$HOME"/* ]] && ! echo "$ROOT" | grep -qE "^$HOME/(Documents|Downloads|Desktop)"; then
  : # outside the protected folders
elif [[ -f "$LOG_DIR/ingest-scheduled.err.log" ]] \
     && grep -q "Operation not permitted" "$LOG_DIR/ingest-scheduled.err.log" 2>/dev/null; then
  cat <<EOF

WARNING: the first run failed with "Operation not permitted".

  $ROOT is inside a macOS-protected folder, and a launchd job is not the
  Terminal app, so macOS denies it access. The schedule is installed and
  correct; it cannot read the repo until one of these is done:

    1. Grant access: System Settings -> Privacy & Security -> Full Disk
       Access -> add /bin/bash (then: launchctl kickstart -k gui/$(id -u)/$LABEL)

    2. Or move the repo out of the protected folder, e.g. to ~/src/JobSuites,
       and run scripts/install-schedule.sh again.
EOF
fi
