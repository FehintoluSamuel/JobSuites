#!/usr/bin/env bash
# Removes the ingest schedule installed by scripts/install-schedule.sh.
set -euo pipefail

LABEL="com.jobsuites.ingest"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true

if [[ -f "$PLIST" ]]; then
  rm -f "$PLIST"
  echo "Removed $PLIST"
else
  echo "No plist at $PLIST"
fi

echo "The schedule is stopped. Logs in logs/ are left in place."
