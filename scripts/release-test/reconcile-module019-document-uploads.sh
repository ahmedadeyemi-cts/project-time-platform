#!/usr/bin/env bash
set -Eeuo pipefail

fail() { echo "ERROR: $*" >&2; exit 1; }

ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-${PROJECTPULSE_ENVIRONMENT:-}}"
EXPECTED_DATABASE_NAME="${PROJECTPULSE_TEST_DATABASE_NAME:-}"
UPLOAD_ROOT="${PROJECTPULSE_UPLOAD_ROOT:-${PROJECT_PULSE_UPLOAD_ROOT:-/opt/project-time-platform/uploads}}"
LEGACY_ROOTS="${PROJECTPULSE_LEGACY_UPLOAD_ROOTS:-/opt/projectpulse/uploads:/opt/project-time-platform/uploads}"
REPORT_PATH="${1:-/tmp/module019-document-reconciliation-$(date -u +%Y%m%dT%H%M%SZ).csv}"

[[ "${ENVIRONMENT,,}" == "test" ]] || fail "This report is restricted to the Test environment."
[[ "$EXPECTED_DATABASE_NAME" =~ ^[A-Za-z_][A-Za-z0-9_]{0,62}$ ]] ||
  fail "PROJECTPULSE_TEST_DATABASE_NAME must be an exact PostgreSQL identifier."
[[ -n "${PGHOST:-}" ]] || fail "PGHOST is not configured."
[[ "${PGPORT:-}" =~ ^[0-9]{1,5}$ ]] || fail "PGPORT is not valid."
[[ "${PGDATABASE:-}" == "$EXPECTED_DATABASE_NAME" ]] ||
  fail "PGDATABASE does not match the protected Test database name."
[[ -n "${PGUSER:-}" ]] || fail "PGUSER is not configured."
[[ -n "${PGPASSWORD:-}" ]] || fail "PGPASSWORD is not configured."
command -v psql >/dev/null || fail "psql is required."
command -v python3 >/dev/null || fail "python3 is required."

METADATA_FILE="$(mktemp)"
trap 'rm -f "$METADATA_FILE"' EXIT

psql --no-psqlrc --set=ON_ERROR_STOP=1 --csv --tuples-only <<'SQL' > "$METADATA_FILE"
SELECT
    document.project_intake_document_id::text AS document_id,
    COALESCE(document.project_id::text, '') AS project_id,
    COALESCE(document.project_intake_request_id::text, '') AS project_intake_request_id,
    COALESCE(document.upload_source, '') AS upload_source,
    COALESCE(document.original_file_name, '') AS original_file_name,
    COALESCE(document.storage_path, '') AS storage_path
FROM project_intake_documents document
WHERE document.is_active = TRUE
  AND COALESCE(document.upload_source, '') <> 'celar_ai_chat_attachment'
ORDER BY document.uploaded_at, document.project_intake_document_id;
SQL

python3 - "$UPLOAD_ROOT" "$LEGACY_ROOTS" "$METADATA_FILE" "$REPORT_PATH" <<'PY'
import csv
import re
import sys
from pathlib import Path

upload_root = Path(sys.argv[1]).expanduser().resolve(strict=False)
legacy_roots = []
for raw in sys.argv[2].split(":"):
    raw = raw.strip()
    if not raw:
        continue
    candidate = Path(raw).expanduser().resolve(strict=False)
    if candidate != upload_root and candidate not in legacy_roots:
        legacy_roots.append(candidate)
metadata_path = Path(sys.argv[3])
report_path = Path(sys.argv[4])
report_path.parent.mkdir(parents=True, exist_ok=True)


def normalize(stored_path: str):
    raw = (stored_path or "").strip()
    if not raw:
        return None, "metadata_path_missing"
    normalized = raw.replace("\\", "/")
    if "\x00" in normalized or re.match(r"^(file|https?):", normalized, re.I):
        return None, "unsafe_path"

    looks_absolute = normalized.startswith("/") or bool(re.match(r"^[A-Za-z]:/", normalized))
    if looks_absolute:
        try:
            absolute = Path(raw).expanduser().resolve(strict=False)
            absolute.relative_to(upload_root)
            normalized = absolute.relative_to(upload_root).as_posix()
        except (ValueError, OSError):
            marker = normalized.lower().rfind("/uploads/")
            if marker < 0:
                return None, "unknown_absolute_path"
            normalized = normalized[marker + len("/uploads/"):]
    else:
        while normalized.startswith("./"):
            normalized = normalized[2:]
        if normalized.lower().startswith("uploads/"):
            normalized = normalized[len("uploads/"):]

    parts = [part for part in normalized.strip("/").split("/") if part]
    if not parts or any(part in {".", ".."} for part in parts):
        return None, "unsafe_path"
    return "/".join(parts), "canonicalized"


fieldnames = [
    "document_id",
    "project_id",
    "project_intake_request_id",
    "upload_source",
    "original_file_name",
    "stored_path",
    "canonical_relative_path",
    "status",
    "resolved_location",
]
summary = {}
with metadata_path.open(newline="", encoding="utf-8") as source, report_path.open(
    "w", newline="", encoding="utf-8"
) as destination:
    reader = csv.DictReader(
        source,
        fieldnames=[
            "document_id",
            "project_id",
            "project_intake_request_id",
            "upload_source",
            "original_file_name",
            "storage_path",
        ],
    )
    writer = csv.DictWriter(destination, fieldnames=fieldnames)
    writer.writeheader()

    for row in reader:
        relative, normalization_status = normalize(row["storage_path"])
        status = normalization_status
        location = ""

        if relative:
            current_candidate = (upload_root / Path(relative)).resolve(strict=False)
            try:
                current_candidate.relative_to(upload_root)
            except ValueError:
                status = "unsafe_path"
            else:
                if current_candidate.is_file() and not current_candidate.is_symlink():
                    status = "current_volume_present"
                    location = str(current_candidate)
                else:
                    matches = []
                    for legacy_root in legacy_roots:
                        candidate = (legacy_root / Path(relative)).resolve(strict=False)
                        try:
                            candidate.relative_to(legacy_root)
                        except ValueError:
                            continue
                        if candidate.is_file() and not candidate.is_symlink():
                            matches.append(candidate)
                    if len(matches) == 1:
                        status = "legacy_volume_copy_candidate"
                        location = str(matches[0])
                    elif len(matches) > 1:
                        status = "ambiguous_legacy_matches"
                        location = " | ".join(str(match) for match in matches)
                    else:
                        status = "physical_file_missing"

        summary[status] = summary.get(status, 0) + 1
        writer.writerow(
            {
                "document_id": row["document_id"],
                "project_id": row["project_id"],
                "project_intake_request_id": row["project_intake_request_id"],
                "upload_source": row["upload_source"],
                "original_file_name": row["original_file_name"],
                "stored_path": row["storage_path"],
                "canonical_relative_path": relative or "",
                "status": status,
                "resolved_location": location,
            }
        )

print(f"MODULE019_RECONCILIATION_REPORT={report_path}")
print(f"MODULE019_UPLOAD_ROOT={upload_root}")
for key in sorted(summary):
    print(f"MODULE019_{key.upper()}={summary[key]}")
PY

echo "MODULE019_RECONCILIATION_MODE=READ_ONLY"
echo "No files, database rows, permissions, mounts, or services were changed."
