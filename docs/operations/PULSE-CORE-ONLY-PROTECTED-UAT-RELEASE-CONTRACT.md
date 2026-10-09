# Core-only Protected UAT release contract (Oracle unavailable)

**Status: proposed design; not an approved release path.** Scope is Protected Test only. The existing full-release controller, its Oracle SOW verification, and Production protections must remain unchanged until a separate reviewed controller is implemented and authorized.

## Operational justification

The merged resilience changes add `/health/live`, `/health/ready`, and `/health/dependencies`, but the running Protected Test frontend currently serves HTML for these paths. `scripts/resilience/check-core-uat-offline.py` rejects that false-positive HTTP 200. Full protected deployment currently stops before API rollout because `celarai.onenecklab.com:443` is unavailable. Oracle-dependent SOW generation must remain **not tested**, never silently marked successful.

## Required admission contract

1. A **new**, isolated core-only deployment controller, not a conditional escape hatch in the existing full SOW release workflow, with least-privilege GitHub environment `test` and Azure credentials restricted to existing Test API/Web resources; no Production or Oracle credentials.
2. Accept exactly the current merged-main 40-character SHA, prove required source/security checks on **that** SHA, bind immutable image digests to the SHA, and reject unapproved branches or PR heads.
3. Admission must be single-flight, with controlled supervisor dispatch, denial for concurrent Test rollouts, audit evidence, deterministic cleanup and re-sealing. No direct manual workflow enablement.
4. Preserve prior revision, image digests, traffic weights and environment contract (especially credentials, networking, managed identity, ingress, replica bounds and database compatibility). Verify migrations are backward-compatible before any API switch; use no destructive or irreversible database migrations.
5. Preflight Test health and gateway backend, deploy first with no user-facing traffic, attest new revision SHA plus `/health/live`, `/health/ready` and `/health/dependencies` **JSON contracts** (not HTML/200 alone); validate DB/core readiness and non-AI authenticated business operations. Promote traffic only after passing canary conditions.
6. Test failure of optional Celar connectivity without disabling security policy validation; verify core routes remain reachable and dependency status is visibly degraded. Maintain existing fail-closed behavior for unapproved AI configuration.
7. Automatic rollback to captured previous revisions on any preflight, canary, auth or health failure. Rollback must not depend on Celar availability. Capture post-rollback health and preserve tamper-evident evidence.
8. Publish two explicit acceptance outcomes: `PULSE_CORE_ONLY_UAT=PASS|FAIL` and `CELAR_SOW_ACCEPTANCE=NOT_EXECUTED`. Neither this lane nor its successes may satisfy full-release, production, or SOW acceptance gates.

## Minimal validation sequence

- Source-contract test: `python3 scripts/resilience/test-core-uat-contract.py`
- Live read-only test: `python3 scripts/resilience/check-core-uat-offline.py` (after the approved release)
- Separately audit Azure Container Apps source SHA and active revision: public version endpoints do not currently expose a commit, so HTTP 200 is not sufficient evidence.
- Run verified non-AI read/write tests in isolated Test fixtures only; no customer deletion or live Production mutation.
- Run failback/recovery drill and review all logs before declaring core acceptance.

## Explicit exclusions

Do not start or alter the Oracle VM, bypass the current Oracle SOW verifier, claim full platform acceptance, change Production, expose laptop-hosted AI to the public internet, or introduce a cloud resource merely to replace the offline VM.
