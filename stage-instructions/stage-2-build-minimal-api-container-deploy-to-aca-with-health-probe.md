# Stage 2: Build minimal API container deploy to ACA with health probe

- **Type:** chore
- **Depends on:** 1

## Objectives

Get the *existing* API container running on ACA against Stage 1's infrastructure,
reachable over its managed-ingress URL, with `/health` returning 200 — before the
database is migrated or the web frontend exists. Proves ACA + ACR + image build/push
works end-to-end with the smallest possible surface.

**Confirmed by verification (2026-09-14):** `deployment/containers/api/Dockerfile`
already exists, exposes port 5080 (`ASPNETCORE_HTTP_PORTS=5080`), matching ADR
0001's assumption exactly — no Dockerfile changes needed. `GET /health`
(`src/backend/ProjectTime.Api/Program.cs:281-286`) returns a static
`{status:"healthy",...}` payload with **no DB dependency**, so it's safe to probe
before Stage 3's migrations have run.

## What to build

- A build/push script (e.g. `deployment/azure/dev/build-and-push-api.sh`) that
  builds `deployment/containers/api/Dockerfile` and pushes to Stage 1's Basic ACR.
- A deploy script (e.g. `deployment/azure/dev/deploy-api-aca.sh`) that creates the
  ACA app in Stage 1's environment: image from ACR, target port 5080,
  `min-replicas=0`, external ingress enabled. DB env vars (`PTP_DB_HOST`,
  `PTP_DB_PORT`, `PTP_DB_NAME`, `PTP_DB_USER`, `PTP_DB_PASSWORD` — confirmed as the
  actual discrete-var shape the app reads, not a single connection string, per
  `Program.cs:2055-2072`) should be set pointing at Stage 1's Postgres instance even
  though it's not migrated yet — `/health` doesn't touch the DB, so the app can
  boot and serve that route regardless.

## Interface contracts

- **Exposes:** a reachable ACA app URL with `GET /health` returning 200. Record the
  URL in `.verity/deploy-access.md`.
- **Consumes:** Stage 1's ACA environment, ACR, and Postgres host/port (env vars
  only — no contract needed since these are plain infra coordinates, not a wire
  seam).

## Testing requirements

No app-level test changes (no application code is modified). Verification is
operational: `curl https://<aca-app-url>/health` returns HTTP 200 with the expected
JSON body.

## Acceptance conditions

- [ ] API image builds and pushes to the Basic ACR successfully
- [ ] ACA app deploys with `min-replicas=0` and scales to zero when idle, per ADR 0001
- [ ] `GET /health` returns 200 over the public ACA ingress URL
- [ ] Existing suite stays green; CI all-green (no application code touched)

## Pipeline test: NO
