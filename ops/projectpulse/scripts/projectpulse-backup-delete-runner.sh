#!/usr/bin/env bash
set -Eeuo pipefail

REQ_ROOT="/opt/project-time-platform/backup-delete-requests"
PENDING="$REQ_ROOT/pending"
PROCESSED="$REQ_ROOT/processed"
FAILED="$REQ_ROOT/failed"
BACKUP_ROOT="/opt/project-time-platform/backups"
RESULTS="$BACKUP_ROOT/results"
REQUEST_ROOT="/opt/project-time-platform/backup-requests"

mkdir -p "$PENDING" "$PROCESSED" "$FAILED"

shopt -s nullglob

for REQUEST_FILE in "$PENDING"/*.json; do
  DELETE_ID="$(basename "$REQUEST_FILE" .json)"

  REQUEST_ID="$(python3 -c 'import json,sys; data=json.load(open(sys.argv[1])); print(data.get("requestId",""))' "$REQUEST_FILE")"

  if [ -z "$REQUEST_ID" ]; then
    mv "$REQUEST_FILE" "$FAILED/$DELETE_ID.json"
    continue
  fi

  RESULT_FILE="$RESULTS/$REQUEST_ID.result.json"
  OUTPUT_FILE="$RESULTS/$REQUEST_ID.output.log"

  PATHS_TO_DELETE=()

  if [ -f "$RESULT_FILE" ]; then
    while IFS= read -r path; do
      [ -n "$path" ] && PATHS_TO_DELETE+=("$path")
    done < <(python3 - "$RESULT_FILE" <<'PY'
import json
import sys

def extract(output, key):
    for line in output.splitlines():
        if line.lower().startswith((key + "=").lower()):
            return line.split("=", 1)[1].strip()
    return ""

with open(sys.argv[1], "r", encoding="utf-8", errors="replace") as handle:
    data = json.load(handle)

output = data.get("output", "") or ""

for key in [
    "backup_directory",
    "backup_bundle",
    "backup_bundle_sha256",
    "database_dump",
    "config_archive",
    "app_archive"
]:
    value = extract(output, key)
    if value:
        print(value)

if data.get("outputFile"):
    print(data["outputFile"])
PY
)
  fi

  PATHS_TO_DELETE+=("$RESULT_FILE")
  PATHS_TO_DELETE+=("$OUTPUT_FILE")
  PATHS_TO_DELETE+=("$REQUEST_ROOT/pending/$REQUEST_ID.json")
  PATHS_TO_DELETE+=("$REQUEST_ROOT/processed/$REQUEST_ID.json")
  PATHS_TO_DELETE+=("$REQUEST_ROOT/failed/$REQUEST_ID.json")

  DELETE_LOG="$REQ_ROOT/processed/$DELETE_ID.deleted.log"
  : > "$DELETE_LOG"

  for candidate in "${PATHS_TO_DELETE[@]}"; do
    [ -z "$candidate" ] && continue

    full_path="$(readlink -f "$candidate" 2>/dev/null || true)"
    [ -z "$full_path" ] && continue

    case "$full_path" in
      "$BACKUP_ROOT"/*|"$REQUEST_ROOT"/*)
        if [ -f "$full_path" ]; then
          rm -f "$full_path"
          echo "deleted_file=$full_path" >> "$DELETE_LOG"
        elif [ -d "$full_path" ]; then
          rm -rf "$full_path"
          echo "deleted_directory=$full_path" >> "$DELETE_LOG"
        else
          echo "not_found=$full_path" >> "$DELETE_LOG"
        fi
        ;;
      *)
        echo "skipped_unsafe_path=$full_path" >> "$DELETE_LOG"
        ;;
    esac
  done

  mv "$REQUEST_FILE" "$PROCESSED/$DELETE_ID.json"
done
