# Stage 3: One-shot DB migration runner for DEV Postgres

- **Type:** chore
- **Depends on:** 1

## Objectives

Apply all 148 existing schema migrations to Stage 1's fresh DEV Postgres instance,
in order, idempotently — producing a schema matching production's shape.

**Confirmed by verification (2026-09-14):** there is **no existing unified migration
runner** in the repo. `database/migrations/` holds 148 `.sql` files tracked via a
`schema_migrations` table, but they're applied via bespoke per-migration scripts
(e.g. `deployment/rocky-linux/apply-migration-NNN.sh`,
`apply-initial-schema.sh:24-38`) invoked one at a time with `psql` and `PTP_DB_*` env
vars. This stage must build the "run them all in order" capability that doesn't
currently exist anywhere — it is new tooling, not a wrapper around something
existing.

## What to build

- A migration-runner script (e.g. `deployment/azure/dev/run-all-migrations.sh`) that:
  - Connects via `psql` using the same `PTP_DB_HOST/PORT/NAME/USER/PASSWORD` env-var
    shape the app itself uses (`Program.cs:2055-2072`) — no new connection-string
    format invented.
  - Iterates `database/migrations/*.sql` in filename order, checking
    `schema_migrations` before applying each (mirroring the idempotency pattern in
    `apply-initial-schema.sh`), so re-running is safe.
  - Fails loudly and stops on the first error rather than continuing past a broken
    migration.
- Run it once against Stage 1's Postgres instance as part of this stage's
  acceptance — this is infra setup, not a reusable CI job (no `.github/workflows/`
  changes; per Verity build-role constraints).

## Interface contracts

- **Exposes:** a migrated Postgres instance with `schema_migrations` fully populated,
  ready for Stage 4's app to connect against with real data access.
- **Consumes:** Stage 1's Postgres host/credentials; the existing
  `database/migrations/*.sql` files (read-only — this stage does not modify any
  migration content).

## Testing requirements

No app-level tests. Verification: after running, `SELECT count(*) FROM
schema_migrations` matches the count of files in `database/migrations/`, and
spot-check a handful of expected tables (e.g. from Module 025's migration 099,
referenced in prior work) exist.

## Acceptance conditions

- [ ] Runner applies all 148 migrations to Stage 1's DEV Postgres with no errors
- [ ] `schema_migrations` row count matches `database/migrations/*.sql` file count
- [ ] Re-running the script against the now-migrated DB is a safe no-op
- [ ] Existing suite stays green; CI all-green (no application code touched)

## Pipeline test: NO
