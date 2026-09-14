# Stage 4: Wire migrated DB + deploy web container, walking-skeleton smoke test

- **Type:** feature
- **Depends on:** 2,3

## Objectives

Complete the walking skeleton: point Stage 2's API app at Stage 3's now-migrated
database, deploy the existing web (nginx/React) container alongside it, and confirm
the full API + web + DB path works end-to-end over the public ACA ingress URL. This
is the "bootable app skeleton, green, deployed" milestone the whole DEV environment
workstream has been building toward — no new application features are introduced.

## What to build

- Update Stage 2's ACA app's DB env vars (if not already correct) now that Stage 3
  has migrated the schema, and confirm a DB-dependent endpoint (not just `/health`)
  responds correctly.
- A build/push/deploy script for the web container (`deployment/containers/web`,
  confirmed nginx-based, `EXPOSE 8080` / `listen 8080;`), mirroring Stage 2's
  API deploy pattern, as a second ACA app in the same environment with
  `min-replicas=0`.
- Wire the web app's API base URL to Stage 2's API app's ACA ingress URL.
- Update `.verity/deploy-access.md` with both final app URLs.

## Interface contracts

- **Exposes:** the full DEV environment — API app URL, web app URL — as the base
  that all future feature stages (planned via Mode B re-intake, not this run) will
  build on.
- **Consumes:** Stage 2's API app, Stage 3's migrated DB, `contracts/ai-provider-interface.md`
  (not exercised yet — no AI-dependent path is touched by the walking skeleton, per
  ADR 0003's deferral).

## Testing requirements

Operational smoke test, authored as a short checklist/script the Operator
(`/verity:ship`) runs post-deploy: load the web app's public URL, confirm the page
renders (not a blank page — the "HTMX stub" failure class the stack-and-topology
guide warns against), and confirm at least one real DB-backed API call succeeds
(e.g. a read-only list endpoint) rather than only checking `/health`.

## Acceptance conditions

- [ ] API app serves a real DB-backed endpoint successfully (not just `/health`)
- [ ] Web app deploys to ACA, `min-replicas=0`, and renders a real page (not blank)
      when loaded via its public ingress URL
- [ ] End-to-end smoke check (above) passes and is documented for `/verity:ship` to
      reuse on every future deploy to this environment
- [ ] `.verity/deploy-access.md` reflects both final ACA app URLs
- [ ] Existing suite stays green; CI all-green
- [ ] N/A — no kill-switch/dark-launch flag: this stage stands up infrastructure and
      wires existing app code to it; it introduces no new user-facing feature
- [ ] N/A — no schema migration: Stage 3 already applied all existing migrations;
      this stage adds no new ones

## Pipeline test: NO
