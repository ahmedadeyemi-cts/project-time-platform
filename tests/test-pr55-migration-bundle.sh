#!/usr/bin/env bash
set -Eeuo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MIGRATOR="$REPO_ROOT/scripts/apply-pr55-test-migrations.sh"
EXPECTED_RELEASE="5b4debe8218560de357f37e567f38aa497482d69"

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

TEST_ROOT="$(mktemp -d)"
trap 'rm -rf "$TEST_ROOT"' EXIT

RELEASE_ROOT="$TEST_ROOT/release"
FAKE_BIN="$TEST_ROOT/bin"
CAPTURED_BUNDLE="$TEST_ROOT/captured-bundle.sql"
PSQL_CALLS="$TEST_ROOT/psql-calls.log"

mkdir -p "$RELEASE_ROOT/database/migrations" "$FAKE_BIN"
printf '%s\n' "$EXPECTED_RELEASE" > "$RELEASE_ROOT/.projectpulse-release-commit"
for migration in \
  034_module_026_crm_erp_integrations.sql \
  035_work_register_055c_055d_split.sql \
  036_work_register_role_scope_and_closeout_handoff.sql; do
  cp "$REPO_ROOT/database/migrations/$migration" "$RELEASE_ROOT/database/migrations/$migration"
done

cat > "$FAKE_BIN/psql" <<'FAKE_PSQL'
#!/usr/bin/env bash
set -Eeuo pipefail

printf 'psql\n' >> "$PR55_TEST_PSQL_CALLS"
for argument in "$@"; do
  case "$argument" in
    --file=*)
      cp "${argument#--file=}" "$PR55_TEST_CAPTURED_BUNDLE"
      ;;
  esac
done
FAKE_PSQL
chmod 0755 "$FAKE_BIN/psql"

run_migrator() {
  PATH="$FAKE_BIN:$PATH" \
  PROJECTPULSE_TEST_DATABASE_URL='postgresql://guarded-test.invalid/projectpulse' \
  PR55_TEST_CAPTURED_BUNDLE="$CAPTURED_BUNDLE" \
  PR55_TEST_PSQL_CALLS="$PSQL_CALLS" \
    "$MIGRATOR" "$RELEASE_ROOT"
}

run_migrator > "$TEST_ROOT/success.out"
[[ "$(wc -l < "$PSQL_CALLS")" -eq 2 ]] ||
  fail "The guarded bundle should perform exactly one preflight and one transaction call."
[[ -f "$CAPTURED_BUNDLE" ]] || fail "The atomic migration bundle was not passed to psql."

for migration_id in \
  034_module_026_crm_erp_integrations \
  035_work_register_055c_055d_split \
  036_work_register_role_scope_and_closeout_handoff; do
  grep -Fq "$migration_id" "$CAPTURED_BUNDLE" ||
    fail "The atomic bundle is missing $migration_id."
done

[[ "$(grep -Ec '^[[:space:]]*BEGIN;[[:space:]]*$' "$CAPTURED_BUNDLE")" -eq 1 ]] ||
  fail "The assembled bundle must contain exactly one transaction BEGIN."
[[ "$(grep -Ec '^[[:space:]]*COMMIT;[[:space:]]*$' "$CAPTURED_BUNDLE")" -eq 1 ]] ||
  fail "The assembled bundle must contain exactly one transaction COMMIT."
grep -Fq 'MIGRATION_036_APPLIED=YES' "$CAPTURED_BUNDLE" ||
  fail "The bundle does not emit migration 036 verification evidence."
grep -Fq 'administrator Work Register grants are incomplete' "$CAPTURED_BUNDLE" ||
  fail "The bundle does not verify migration 036 administrator grants."

assert_rejected_before_psql() {
  local scenario="$1"
  : > "$PSQL_CALLS"
  if run_migrator > "$TEST_ROOT/$scenario.out" 2> "$TEST_ROOT/$scenario.err"; then
    fail "$scenario unexpectedly passed."
  fi
  [[ ! -s "$PSQL_CALLS" ]] || fail "$scenario reached psql instead of failing closed."
}

cp "$RELEASE_ROOT/database/migrations/036_work_register_role_scope_and_closeout_handoff.sql" \
  "$TEST_ROOT/migration-036.original"
printf '\n-- tampered\n' >> "$RELEASE_ROOT/database/migrations/036_work_register_role_scope_and_closeout_handoff.sql"
assert_rejected_before_psql tampered-migration-036
mv "$TEST_ROOT/migration-036.original" \
  "$RELEASE_ROOT/database/migrations/036_work_register_role_scope_and_closeout_handoff.sql"

mv "$RELEASE_ROOT/database/migrations/036_work_register_role_scope_and_closeout_handoff.sql" \
  "$TEST_ROOT/migration-036.missing"
assert_rejected_before_psql missing-migration-036
mv "$TEST_ROOT/migration-036.missing" \
  "$RELEASE_ROOT/database/migrations/036_work_register_role_scope_and_closeout_handoff.sql"

printf '%s\n' '0000000000000000000000000000000000000000' > "$RELEASE_ROOT/.projectpulse-release-commit"
assert_rejected_before_psql wrong-release-marker

echo 'PR55_MIGRATION_BUNDLE_TEST=PASS'
