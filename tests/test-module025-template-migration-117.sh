#!/usr/bin/env bash
set -euo pipefail

# Disposable local database only; never accept a remote database target.
case "${PGHOST:-127.0.0.1}" in 127.0.0.1|localhost) ;; *) echo "Use a disposable local PostgreSQL server." >&2; exit 1;; esac
export PGHOST="${PGHOST:-127.0.0.1}"
export PGUSER="${PGUSER:-postgres}"
test_database="module025_template_test_${BASHPID}"
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
createdb "$test_database"
trap 'dropdb --if-exists "$test_database" >/dev/null' EXIT
export PGDATABASE="$test_database"
psql -v ON_ERROR_STOP=1 <<'SQL'
CREATE TABLE app_users(user_id uuid PRIMARY KEY);
CREATE TABLE schema_migrations(migration_id text PRIMARY KEY, description text, applied_at timestamptz);
INSERT INTO app_users VALUES ('00000000-0000-0000-0000-000000000001');
SQL
psql -v ON_ERROR_STOP=1 -f "$repository_root/database/migrations/117_module025_template_candidates.sql"
psql -v ON_ERROR_STOP=1 -f "$repository_root/database/migrations/117_module025_template_candidates.sql"
psql -v ON_ERROR_STOP=1 <<'SQL'
INSERT INTO module025_template_candidates
    (template_version_id,owner_user_id,owner_display_name,document_kind,customer_program,version_number,
     label,change_notes,file_name,content_sha256,file_content,validation_json)
VALUES ('10000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000001','Manager','gsd','standard',1,
        'Original','Mapping review','original.xlsx',repeat('a',64),decode('504b','hex'),'{}');
DO $$ BEGIN
    BEGIN
        UPDATE module025_template_candidates SET label='Changed';
        RAISE EXCEPTION 'Test failed: mutable template label';
    EXCEPTION WHEN raise_exception THEN
        IF SQLERRM NOT LIKE 'Module 025 template candidates are immutable%' THEN RAISE; END IF;
    END;
    BEGIN
        DELETE FROM module025_template_candidates;
        RAISE EXCEPTION 'Test failed: deleted original';
    EXCEPTION WHEN raise_exception THEN
        IF SQLERRM NOT LIKE 'Module 025 template candidates are immutable%' THEN RAISE; END IF;
    END;
    BEGIN
        INSERT INTO module025_template_candidates SELECT template_version_id,owner_user_id,owner_display_name,owner_team_name,
          organization_visible,document_kind,customer_program,version_number,label,change_notes,file_name,content_sha256,
          file_content,validation_json,'active',created_at FROM module025_template_candidates;
        RAISE EXCEPTION 'Test failed: active template allowed';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
    IF (SELECT count(*) FROM module025_template_candidates) <> 1 THEN RAISE EXCEPTION 'Original lost'; END IF;
END $$;
SQL
rollback_log="$(mktemp)"
if psql -v ON_ERROR_STOP=1 -f "$repository_root/database/rollback/117_module025_template_candidates_rollback.sql" >"$rollback_log" 2>&1; then
  rm -f "$rollback_log"
  echo "Rollback should refuse retained template evidence." >&2
  exit 1
fi
if ! rg -q 'Rollback refused: retained template candidates exist' "$rollback_log"; then
  cat "$rollback_log"
  rm -f "$rollback_log"
  exit 1
fi
rm -f "$rollback_log"
psql -v ON_ERROR_STOP=1 -c "DO \$\$ BEGIN IF (SELECT count(*) FROM module025_template_candidates) <> 1 THEN RAISE EXCEPTION 'Rollback lost originals'; END IF; END \$\$;"
echo 'PASS: migration idempotence, immutable originals, activation constraint, and retention-safe rollback'
