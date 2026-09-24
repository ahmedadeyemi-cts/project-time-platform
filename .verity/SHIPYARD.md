# Shipyard (verity:ship) machinery

This wires the project into verity's Release/Deploy Operator flow. Everything here
is container-digest based: the release builds images once, scans them, and records
their digests; deploy runs those **byte-identical** digests; prod promotion reuses
the same digests staging proved.

## Files

| File | Role |
| --- | --- |
| `.github/workflows/release.yml` | On a `vX.Y.Z` tag: build `web` + `api` (existing Dockerfiles) once → Trivy scan → push to GHCR → attach `release-digests.json` to a GitHub Release. |
| `scripts/verity/pin-digests.sh` | Pull that manifest → write `.verity/release.env` (pinned by-digest refs). Never hand-copy digests. |
| `.verity/deploy.compose.yml` | Deploy topology (db + api + web) consuming the pinned digests. |
| `.verity/deploy.env(.example)` | Non-secret operator config + pointer to where the DB password lives. |
| `deploy.sh` | pull pinned digests → backup → bootstrap → (baseline) → forward-migrate → up → verify (self-contained health/version/401 checks). |
| `scripts/verity/build-local.sh` | Build both images locally + write `.verity/release.env` so `./deploy.sh` runs offline (local iteration). |
| `.verity/db-bootstrap.sql` | Provisioning prerequisites the migrations assume (e.g. the `ptp_app` role). Idempotent; runs before migrations. |
| `.verity/smoke.json` | Playwright UI "observably-works" flows (`verity smoke run`). Needs `playwright` resolvable from repo root. |
| `scripts/021-release-smoke.sh` | Pre-existing HTTP fleet check for the onenecklab VM (hard-coded URLs); kept separate — `deploy.sh` has its own self-contained verify. |

## Runbook

```bash
verity release changelog                 # preview
verity release cut --bump patch|minor|major   # tag -> triggers release.yml
scripts/verity/pin-digests.sh vX.Y.Z     # pin digests -> .verity/release.env
./deploy.sh                              # deploy to the target env
verity smoke run --base-url <url>        # UI gate; verified:false => STOP
# promote: same digests to prod (prod_promote=confirm), then:
verity status set version <version>
verity status set environments.prod.digest <sha256>
verity status set rollback_from <previous-digest>
```

Rollback: `scripts/verity/pin-digests.sh <previous-tag> && ./deploy.sh` (safe —
migrations are additive-only).

## Verified locally (2026-09-24)

- ✅ `web` + `api` images build from the existing Dockerfiles.
- ✅ `docker compose -f .verity/deploy.compose.yml` brings up db+api+web healthy.
- ✅ App serves: `/` 200, `/health` 200 (`{"status":"healthy"}`), `/api/version` 200
  (v0.9.0, .NET 10), protected `/api/customers/overview` 401.
- ✅ `verity smoke run --base-url http://localhost:8080` → `verified: true` (all flows),
  with `playwright` installed at repo root (`node_modules/` is git-ignored).

## Open items before a real release can run

1. **DB provisioning model — DEFERRED (the one open item).** The
   `database/migrations/*.sql` set is NOT a clean replay from `001` on an empty DB
   (verified: 119 ok / 53 fail from scratch). It assumes a **provisioned baseline**:
   the `ptp_app` role (now in `db-bootstrap.sql`), a project-managed
   `schema_migrations` table, and a runner that injects session values for some
   migrations (e.g. `087` needs a session-injected PBKDF2 value). Naive lexical
   replay also hits ordering/guard cascades (e.g. `019m-aa` needs
   `engineering_resource_requests`) and one real bug (`012` inserts
   `utilization_bucket='holiday'` but the `005` check constraint forbids it).
   So a fresh container DB needs a **schema baseline captured from a known-good DB**,
   then forward-migrate only. `deploy.sh` supports this: drop a dump at
   `.verity/db-baseline.dump` (custom format) or `.verity/db-baseline.sql` (plain),
   set the covered migration in `.verity/db-baseline.marker` (currently `125...`).

   **DB topology (as of 2026-09-24):** the *web host* is OCI
   (`projectpulse-test.onenecklab.com` → 167.234.223.32, Oracle), but the DB is
   mid-migration to Azure. Live source = OCI PostgreSQL 13 (~31 MB). Azure target =
   `pg-phd-test-w3-7825cc` (PG 16, db `project_health_dashboard`, in sub
   `cd32baeb…`, public access DISABLED); a source export was restored into it
   (checkpoint AZ-05C2) and also lives in Blob `stphdtest7825cc/database-exports`.
   Both Azure copies are a **July snapshot** (commit `5a221da`), behind `main`.
   See `docs/azure-migration/STATUS.md`.

   **To resume — pick a source:**
   - *Azure blob export (easiest, in-your-control):* self-grant `Storage Blob Data
     Reader` on `stphdtest7825cc` (Owner can), download the export from
     `database-exports` → `.verity/db-baseline.dump`; then restore, read its
     `schema_migrations` to set the marker, forward-migrate the gap to 125.
   - *Fresh OCI dump (current data):* have the OCI admin run
     `sudo -u postgres pg_dump -Fc -Z6 ProjectPulse` (or grab the newest
     `/opt/project-time-platform/backups/manual-deploy/*/ProjectPulse.dump`).
   - *Dump the Azure PG directly:* temporarily enable public access + firewall on
     `pg-phd-test-w3-7825cc`, then `pg_dump` it.
2. **Deploy target** — compose runs great locally; for real staging/prod point it at
   a host, or adapt to Azure ACA (`.verity/deploy-access.md` env is "not live yet").
3. **Staging TLS cert expired** on `projectpulse-test.onenecklab.com` (server-side).
4. **Registry** defaults to GHCR under the repo owner; swap to ACR in `release.yml`
   if preferred.
5. **Governance controllers** — adding `release.yml` will be seen by the repo's many
   source-boundary/exact-scope CI controllers at PR time; register its scope like the
   other workflows.
