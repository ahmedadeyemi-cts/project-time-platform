# Production Readiness Foundation

Prepare Pulse for a clean production launch in roughly 2–3 months while retaining Protected UAT for development and testing. This foundation produces offline evidence and a work queue. It does not provision infrastructure, change runtime settings, execute SQL, delete data, import users, send messages, or deploy a release.

**Release sequencing:** hold this PR for the current deployment to finish. Recheck current main and PR checks before merge. Creating the PR runs read-only CI only. No merge, workflow dispatch, cancellation, or Production activation is included.

## Use it now

From the repository root, with Python 3.10 or later:

```sh
python3 scripts/production-readiness/check_foundation.py --inventory > /tmp/pulse-initialization-inventory.json
python3 scripts/production-readiness/check_foundation.py > /tmp/pulse-foundation-report.json
python3 -m unittest discover -s tests/production-readiness -p 'test_*.py' -v
```

The second command intentionally exits **2** with the committed example: environment identifiers are unresolved and most scripts still need review. Exit 0 means only that the declared foundation inputs pass offline checks. `production_ready` and `live_environment_verified` remain false in every report. This is not an approval or deployment gate connected to the application.

Do not commit generated operational reports containing environment identities unnecessarily. The report does not echo supplied resource identifiers or credentials. Never provide connection strings, passwords, tokens, SAS URLs, or secret values to these tools.

## Initialization review

`initialization-review.json` inventories every `.sql` file under `database/migrations`, `database/seed-data`, `database/demo`, and `deployment/rocky-linux`, including nested files. SHA-256 hashes detect additions, changes and removals. Signal matching is deliberately a conservative triage aid, not a SQL parser or an assertion that an unflagged file is safe.

Decisions:

- `review_required`: no production decision yet; blocks completion of the foundation review.
- `exclude_from_production`: proposed exclusion from the future initialization plan, with rationale. It does not modify existing migration execution.
- `candidate_schema_or_reference`: reviewed candidate containing required schema/reference changes, with rationale. This is not permission to run it, a dependency ordering, or proof that it succeeds on an empty database.

Known initial findings:

| Source | Finding | Preparation action |
|---|---|---|
| `009_project_task_assignment_foundation_seed.sql` | Creates development identities, a sample PSA project, tasks and assignments | Exclude from proposed Production initialization; verify downstream dependencies |
| `010_psa_demo_assignment_visibility.sql` | Adjusts dates on sample assignments | Exclude with 009 |
| `015_role_enforcement_and_user_switcher.sql` | Mixes permissions with demo identities and role assignments | Separate required behavior from test-only identities in a later reviewed change |
| `database/demo/*.sql` | Explicit demo data | Keep for UAT only |
| `database/seed-data/*.sql` | Includes useful non-project categories and work-location defaults | Business review; do not blanket-delete or skip all seed files |
| `.github/workflows/projectpulse-deploy-production.yml` | Currently only an approval-gated placeholder | Real production deployment, identity verification and rollback are future work |

Review the complete file and its dependencies before changing a decision. For modified files, inspect the diff, update the hash from a fresh inventory, and return the decision to `review_required` until assessed. Add new files explicitly; document intentional removals. Never automatically carry a prior review across a new hash.

This inventory is **not an executable migration list**. Filename sorting is not migration dependency resolution. SQL in shell scripts, backend startup/bootstrap paths, generated code, stored-function callers and external infrastructure is outside the inventory and remains an explicit acceptance item. Do not mark skipped migrations applied merely to silence an existing migration runner. A fresh-install runner must resolve schema prerequisites and accurately record what it executed.

## Environment separation

Copy `environment.example.json` to a local file and record fully qualified non-secret resource identifiers for Test and proposed Production. Example database format: `azure/subscriptions/SUB/resourceGroups/RG/postgresql/SERVER/databases/DB`. Identify a RAG collection with its service/instance and collection name; identify each logical queue with its hosting service or database and queue identity. Use one canonical format for both environments, including actual storage container/mount identities.

```sh
python3 scripts/production-readiness/check_foundation.py --environment /absolute/path/to/environment.json > /tmp/pulse-foundation-report.json
```

The checker rejects missing identities, equal Test/Production identities after case/trailing-slash normalization, credential-like values, and preparation flags not explicitly false. It cannot detect aliases, overlapping storage paths, permissions, live connectivity, or IDs that were entered incorrectly. Verify those independently against infrastructure. A shared physical service needs separately controlled logical namespaces and isolation evidence; a different display name is not enough.

The four `*_enabled` flags are **declarations in a planning file, not runtime controls**. Setting them false does not stop any existing mail, integration or background process. Keep any newly provisioned production environment disconnected from outbound delivery until actual runtime controls are configured and tested. Existing UAT flags are not changed.

## Deliverables and next work

- [Configuration inventory](CONFIGURATION-INVENTORY.md): retain, recreate, reimport, or start empty.
- [Preparation and cutover checklist](GO-LIVE-CHECKLIST.md): owners, acceptance and evidence across the next 2–3 months.
- Offline checker and tests: catalog drift, unresolved decisions, declared isolation and safe preparation defaults.

Next PRs should implement reviewed production initialization, infrastructure/secrets, operational backup/restore/monitoring, and the actual deployment pipeline. Rehearse on a disposable, isolated empty environment before go-live. Do not wipe UAT or disable immutable evidence protections to simulate a clean install.
