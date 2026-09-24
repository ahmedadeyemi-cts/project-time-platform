#!/usr/bin/env bash
# Verity Shipyard deploy.
#
#   pull pinned digests -> back up -> additive migrate -> up -> verify
#
# Runs the byte-identical, digest-pinned images produced by release.yml against a
# target environment. Migrations are additive-only (never runs database/rollback/),
# so re-running is safe and rollback = re-pin the previous digests + re-run.
#
# Usage:
#   scripts/verity/pin-digests.sh vX.Y.Z   # writes .verity/release.env
#   ./deploy.sh                            # deploy the pinned release
set -Eeuo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

COMPOSE="docker compose -f .verity/deploy.compose.yml --env-file .verity/deploy.env"

# --- config ---
[ -f .verity/deploy.env ]  || { echo "Missing .verity/deploy.env (copy .verity/deploy.env.example)"; exit 1; }
[ -f .verity/release.env ] || { echo "Missing .verity/release.env — run scripts/verity/pin-digests.sh first"; exit 1; }
set -a; . .verity/deploy.env; . .verity/release.env; set +a

VERIFY_BASE_URL="${VERIFY_BASE_URL:-http://localhost:${WEB_PORT:-8080}}"
echo "== Deploying ${RELEASE_TAG:-?} =="
echo "  web: ${WEB_IMAGE}"
echo "  api: ${API_IMAGE}"
echo "  verify: ${VERIFY_BASE_URL}"

# --- pull pinned digests ---
# --ignore-pull-failures so locally-built images (scripts/verity/build-local.sh)
# work too; released digests still pull normally.
echo "== Pull pinned digests =="
$COMPOSE pull --ignore-pull-failures

# --- backup (best-effort snapshot of current DB before migrating) ---
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
BACKUP_DIR="${PROJECTPULSE_BACKUP_ROOT:-.verity/backups}/${STAMP}"
mkdir -p "$BACKUP_DIR"
if $COMPOSE ps db --status running >/dev/null 2>&1; then
  echo "== Backup DB -> ${BACKUP_DIR}/ProjectPulse.dump =="
  $COMPOSE exec -T db pg_dump -Fc -U "${POSTGRES_USER:-projectpulse}" "${POSTGRES_DB:-ProjectPulse}" \
    > "${BACKUP_DIR}/ProjectPulse.dump" || echo "WARN: pre-deploy backup skipped (db not ready)"
fi

# --- additive migrate (never runs database/rollback/) ---
echo "== Bring up db and apply pending migrations (additive-only) =="
$COMPOSE up -d db
# wait for healthy
for _ in $(seq 1 40); do
  $COMPOSE exec -T db pg_isready -U "${POSTGRES_USER:-projectpulse}" -d "${POSTGRES_DB:-ProjectPulse}" >/dev/null 2>&1 && break
  sleep 2
done

psql_db() { $COMPOSE exec -T db psql -v ON_ERROR_STOP=1 -U "${POSTGRES_USER:-projectpulse}" -d "${POSTGRES_DB:-ProjectPulse}" "$@"; }

# Provisioning prerequisites (roles etc.) the migration set assumes exist.
echo "  bootstrap: prerequisites (.verity/db-bootstrap.sql)"
psql_db < .verity/db-bootstrap.sql

# Optional baseline. This project's migrations are NOT a clean replay from 001 on
# an empty DB (they assume a provisioned baseline + a runner that injects session
# values for some migrations). For a brand-new environment, restore a schema
# baseline captured from a known-good DB, and record which migration it covers in
# .verity/db-baseline.marker (migrations <= that filename are then skipped).
BASELINE_MARKER=""
if [ -f .verity/db-baseline.dump ] || [ -f .verity/db-baseline.sql ]; then
  # Restore only into an empty DB (no app tables yet).
  tables="$(psql_db -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema='public';" | tr -d '[:space:]')"
  if [ "${tables:-0}" = "0" ]; then
    if [ -f .verity/db-baseline.dump ]; then
      echo "  restoring baseline (.verity/db-baseline.dump, custom format)"
      $COMPOSE exec -T db pg_restore --no-owner --no-privileges --clean --if-exists \
        -U "${POSTGRES_USER:-projectpulse}" -d "${POSTGRES_DB:-ProjectPulse}" < .verity/db-baseline.dump
    else
      echo "  restoring baseline (.verity/db-baseline.sql, plain)"
      psql_db < .verity/db-baseline.sql
    fi
  fi
  [ -f .verity/db-baseline.marker ] && BASELINE_MARKER="$(tr -d '[:space:]' < .verity/db-baseline.marker)"
fi

psql_db -c "CREATE TABLE IF NOT EXISTS verity_schema_migrations (filename text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());"

applied=0
for f in $(ls database/migrations/*.sql | sort); do
  base="$(basename "$f")"
  # Skip anything already covered by the baseline (lexical <=).
  if [[ -n "$BASELINE_MARKER" && ! "$base" > "$BASELINE_MARKER" ]]; then continue; fi
  already="$(psql_db -tAc "SELECT 1 FROM verity_schema_migrations WHERE filename = '${base}';")"
  if [ "$already" = "1" ]; then continue; fi
  echo "  applying ${base}"
  psql_db < "$f"   # additive-only; stops the deploy on error (never runs rollback/)
  psql_db -c "INSERT INTO verity_schema_migrations(filename) VALUES ('${base}');"
  applied=$((applied+1))
done
echo "  ${applied} forward migration(s) applied${BASELINE_MARKER:+ (baseline: ${BASELINE_MARKER})}"

# --- up ---
echo "== Up (web + api on pinned digests) =="
$COMPOSE up -d

# --- verify ---
echo "== Verify: health =="
ok=""
for _ in $(seq 1 30); do
  if curl -fsS -k --max-time 10 "${VERIFY_BASE_URL}/health" >/dev/null 2>&1; then ok=1; break; fi
  sleep 2
done
[ -n "$ok" ] || { echo "FAIL: ${VERIFY_BASE_URL}/health never became healthy"; exit 1; }

echo "== Verify: behavioral smoke (health/version 200, protected endpoints 401) =="
# Self-contained against VERIFY_BASE_URL. scripts/021-release-smoke.sh is the
# richer, hard-coded fleet check for the onenecklab VM; kept separate on purpose.
verify_code() { # label url expected
  local code
  code="$(curl -k -s -o /dev/null -w '%{http_code}' --max-time 15 "$2" || echo 000)"
  if [ "$code" = "$3" ]; then echo "PASS: $1 -> $code"; else
    echo "FAIL: $1 -> $code (expected $3)"; return 1; fi
}
fail=0
verify_code "health"        "${VERIFY_BASE_URL}/health"                       200 || fail=1
verify_code "api version"   "${VERIFY_BASE_URL}/api/version"                  200 || fail=1
verify_code "auth guard"    "${VERIFY_BASE_URL}/api/customers/overview"       401 || fail=1
[ "$fail" = 0 ] || { echo "FAIL: behavioral smoke reported failures"; exit 1; }

cat <<EOF

== Deploy complete for ${RELEASE_TAG:-?} ==
Next (UI observably-works gate):
  verity smoke run --base-url ${VERIFY_BASE_URL}
Then record runtime truth:
  verity status set version ${RELEASE_VERSION:-?}
  verity status set environments.<env>.digest <sha256>
  verity status set rollback_from <previous-digest>
Rollback if needed:
  scripts/verity/pin-digests.sh <previous-tag> && ./deploy.sh
EOF
