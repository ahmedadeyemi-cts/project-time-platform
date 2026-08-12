#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MODULE="$ROOT/src/backend/ProjectTime.Api/Modules/ProjectWorkspaceModule019Repair.cs"
WRAPPER="$ROOT/src/backend/ProjectTime.Api/Modules/ProjectWorkspaceModule.cs"
MIGRATION="$ROOT/database/migrations/085_module_019_document_access_storage_repair.sql"
MIGRATION_TEST="$ROOT/tests/test-module019-document-access-migration-085.sh"
REPORT="$ROOT/scripts/release-test/reconcile-module019-document-uploads.sh"
APPLY="$ROOT/scripts/release-test/apply-085.sh"

fail() { echo "FAIL: $*" >&2; exit 1; }

for file in "$MODULE" "$WRAPPER" "$MIGRATION" "$MIGRATION_TEST" "$REPORT" "$APPLY"; do
  [[ -s "$file" ]] || fail "Missing required repair artifact: $file"
done

bash -n "$REPORT"
bash -n "$APPLY"
bash -n "$MIGRATION_TEST"

grep -q 'scope.direct_project_assignment' "$MODULE" || fail "Direct project assignment scope is missing."
grep -q 'scope.direct_service_request_assignment' "$MODULE" || fail "Direct SR assignment scope is missing."
[[ "$(grep -c 'scope.direct_project_assignment' "$MODULE")" -ge 3 ]] || fail "List/download predicates are not aligned."
[[ "$(grep -c 'scope.direct_service_request_assignment' "$MODULE")" -ge 3 ]] || fail "SR list/download predicates are not aligned."
grep -q 'effective_start_date <= CURRENT_DATE' "$MODULE" || fail "Active assignment start-date enforcement is missing."
grep -q 'effective_end_date IS NULL OR' "$MODULE" || fail "Active assignment end-date enforcement is missing."
grep -q 'NormalizeStoredPath' "$MODULE" || fail "Canonical path resolver is missing."
! grep -q 'Directory\.EnumerateFiles' "$MODULE" || fail "Unsafe recursive filename fallback remains present."
grep -q 'HasReparsePoint' "$MODULE" || fail "Symlink/reparse protection is missing."

grep -q "085_module_019_document_access_storage_repair" "$MIGRATION" || fail "Migration identity is missing."
grep -q 'BEFORE INSERT OR UPDATE OF stored_file_path' "$MIGRATION" || fail "055C canonical-path trigger is missing."
grep -q 'BEFORE INSERT OR UPDATE OF storage_path' "$MIGRATION" || fail "055D canonical-path trigger is missing."
grep -q 'projectpulse085_normalize_upload_path' "$MIGRATION" || fail "Database path normalizer is missing."
[[ "$(grep -c '^BEGIN;$' "$MIGRATION")" == 1 ]] || fail "Migration must have one top-level BEGIN."
[[ "$(grep -c '^COMMIT;$' "$MIGRATION")" == 1 ]] || fail "Migration must have one top-level COMMIT."
grep -q 'postgres:16-alpine' "$MIGRATION_TEST" || fail "PostgreSQL 16 migration execution coverage is missing."
grep -q 'MODULE019_DOCUMENT_ACCESS_MIGRATION_085=PASS' "$MIGRATION_TEST" || fail "Migration test pass attestation is missing."

grep -qi 'restricted to the Test environment' "$REPORT" || fail "Reconciliation report lacks a Test guard."
grep -q 'MODULE019_RECONCILIATION_MODE=READ_ONLY' "$REPORT" || fail "Read-only report attestation is missing."
! grep -Eq '(^|[[:space:]])(rm -rf|mv|rsync|cp)([[:space:]]|$)' "$REPORT" || fail "Reconciliation report contains a file-changing command."
! grep -Eqi '(^|[[:space:]])(UPDATE|DELETE|INSERT|TRUNCATE)[[:space:]]' "$REPORT" || fail "Reconciliation report contains a database mutation."

grep -q 'ProjectWorkspaceModule019Repair.MapEndpoints' "$WRAPPER" || fail "Module 019 wrapper does not activate the repair."

python3 - "$MODULE" <<'PY'
import sys
from pathlib import Path
text = Path(sys.argv[1]).read_text(encoding="utf-8")
if text.count('"""') % 2:
    raise SystemExit("FAIL: unbalanced C# raw-string delimiters")
if text.count("{") != text.count("}"):
    raise SystemExit("FAIL: unbalanced C# braces")
print("MODULE019_CSHARP_STRUCTURAL_CHECK=PASSED")
PY

echo "MODULE019_DOCUMENT_ACCESS_REPAIR_085_CONTRACT=PASSED"
