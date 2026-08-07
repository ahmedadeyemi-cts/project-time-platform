# Group 2B — Provider-Neutral Production Resilience

## Scope

This source package makes Modules 014, 015, and 017 consume the provider-neutral platform abstraction introduced by Group 2A. It creates one planning and reporting contract for production readiness, recovery, continuity, redundancy, and failover without changing the existing route ownership or replacing the operational controls already present in each module.

This package is source-only. It does not deploy, run a database migration, change Azure or Container Apps resources, change backup targets, execute a restore, or initiate failover.

## Existing responsibilities and assumptions inspected

The existing modules remain valuable operational surfaces, but they were built around assumptions tied to the current implementation:

| Module | Existing operational responsibility | Assumptions that needed an abstraction boundary |
|---|---|---|
| Module 014 | Backup and disaster-recovery settings, backup execution, status, and run history | Local/server backup bundles, SFTP targets, Azure Blob targets, and server-managed schedules |
| Module 015 | Restore-point selection and non-production restore validation | Pulse/server wording, local bundle paths, PostgreSQL `pg_restore` inspection, and a server-side restore runbook |
| Module 017 | Replication and synchronization readiness | A named peer server, peer host/IP, peer URL, and a future second or third Pulse server |

No Oracle-only contract was found in these three current web experiences. The provider-specific assumptions are primarily local-server, Azure, PostgreSQL, and peer-node concepts. Group 2B keeps those operational controls intact while placing provider-neutral production planning above them.

## Recommended responsibilities implemented

### Module 014 — Environment and production-readiness planning

Module 014 now leads with a provider-neutral current-versus-target planning view. It shows the current platform adapter, current environment, region, workload kind, deployment, release, observed replica count, current single-instance limitations, planned production provider, planned region, target workload, target replica count, database and storage topology, network boundary, observability design, release approval model, rollback design, readiness blockers, responsible owners, and evidence and approval history.

### Module 015 — Backup, recovery, restoration, and continuity

Module 015 now consolidates recovery point objective, recovery time objective, last successful backup, last successful recovery test, backup policy, frequency, retention, location, storage readiness, recovery runbook, continuity communication plan, readiness blockers, owners, and approval evidence. A successful API read or configured storage target is not presented as a successful recovery test.

### Module 017 — Availability, regions, replicas, redundancy, and failover

Module 017 now shows application replicas reported by the Group 2A adapter, observed and target regions, database replica or managed high-availability evidence, storage replication separately from storage availability, failover mode, failover runbook, last failover test, failover prerequisites, blockers, owners, and approval evidence.

## Shared provider-neutral contract

All three modules use `PlatformOperationsModule.BuildSnapshotAsync` from Group 2A. The current adapter remains authoritative for current provider, environment, region, workload, runtime deployment, dependencies, replicas, and bounded operational evidence. Target-state and governance values are operator-recorded. Unknown values are returned as `not_recorded`; they are never guessed from Azure, local host, or process metadata.

Azure-specific values remain behind the Azure adapter. A future adapter can populate the same current-platform, replica, region, dependency, and evidence fields without rebuilding the Module 014, 015, or 017 screens.

## Reporting APIs

The package adds five administrator-only, read-only endpoints:

- `GET /api/system/backup-dr/production-planning`
- `GET /api/system/restore-validation/recovery-continuity`
- `GET /api/system/replication-sync/redundancy-failover`
- `GET /api/system/backup-dr/resilience-report`
- `GET /api/system/backup-dr/resilience-report/export`

The consolidated report is designed for later reporting and governance use. It includes all three module contracts, the Group 2A platform adapter contract version, sanitized provider details already exposed by Group 2A, access classification, security assertions, blockers, owners, evidence history, and approval history. The export endpoint returns the same contract as formatted JSON.

The reporting contract also identifies the existing operational source APIs—`/api/system/backup-dr/status`, `/api/system/restore-validation/status`, and `/api/system/replication-sync/status`—so future reporting can correlate planning evidence with each module's live operational view without creating a second backup, restore, or replication control plane.

The APIs preserve the existing Module 013 inventory ownership rules because their prefixes stay under the established Module 014, 015, and 017 route groups.

## Optional provider-neutral configuration evidence

The runtime may supply target design and governance evidence through environment configuration. Representative fields include:

- `PROJECTPULSE_TARGET_PLATFORM_PROVIDER`, `PROJECTPULSE_TARGET_REGION`, `PROJECTPULSE_TARGET_WORKLOAD_KIND`, and `PROJECTPULSE_TARGET_REPLICA_COUNT`
- `PROJECTPULSE_RPO_MINUTES`, `PROJECTPULSE_RTO_MINUTES`, `PROJECTPULSE_LAST_SUCCESSFUL_BACKUP_AT`, and `PROJECTPULSE_LAST_SUCCESSFUL_RECOVERY_TEST_AT`
- `PROJECTPULSE_DATABASE_REPLICA_STATUS`, `PROJECTPULSE_STORAGE_REPLICATION_STATUS`, `PROJECTPULSE_FAILOVER_REGION`, and `PROJECTPULSE_LAST_FAILOVER_TEST_AT`
- `PROJECTPULSE_PRODUCTION_READINESS_OWNER`, `PROJECTPULSE_RECOVERY_OWNER`, `PROJECTPULSE_FAILOVER_OWNER`, and `PROJECTPULSE_PRODUCTION_RESILIENCE_APPROVER`
- Module-specific approval timestamps, approvers, and references using `PROJECTPULSE_MODULE_014_*`, `PROJECTPULSE_MODULE_015_*`, and `PROJECTPULSE_MODULE_017_*`

These fields contain planning and evidence references, not provider credentials. Secret values, raw exception details, request bodies, query strings, and provider credentials are excluded by the shared Group 2A security contract.

## Web integration and branding

`PlatformResiliencePlanningPanel.jsx` is installed additively into the existing Module 014, 015, and 017 components during the established frontend prebuild and predev generation sequence. The installer is idempotent and does not rewrite `App.jsx`, `main.jsx`, navigation, or the module registry. The panel uses the existing US Signal logo data asset and ProjectPulse module-standard stylesheet, with responsive enterprise presentation and explicit degraded-state messaging.

The operational settings and controls already present in each module remain below the new planning panel. A planning API failure does not hide or replace those existing controls.

## Shared-file overlap

One shared backend registration point is required: `ProjectTime.Api.csproj` adds `app.MapPlatformProductionResilienceEndpoints();` to the generated Program registration sequence before `app.Run()`. The frontend `package.json` adds the idempotent installer and validator to the existing predev, prebuild, and full build chains.

No change is made to `App.jsx`, `main.jsx`, navigation files, module registries, migration registration, deployment scripts, Azure definitions, or the protected active deployment files.

## Migration and dependency declaration

No migration is included or required. Group 2B reads runtime, adapter, and operator-recorded configuration evidence. It does not create or alter database schema or data.

Group 3 financial workspace work is intentionally outside this package. Modules 018, 019, 036, and 055B, Module 005 expenses, Module 026 SELL integration, project assignments, documents, time entries, rates, and allocations are not modified here. PR #187 is treated as completed dependency history rather than a concurrent connection-foundation change.

## Validation

The source validator confirms that the package:

- consumes the Group 2A abstraction and actual-session administrator authorization;
- exposes exactly five GET-only APIs and no mutation endpoint;
- includes current versus target, single-instance, RPO, RTO, recovery-test, replica, storage replication, regional, failover, blocker, owner, evidence, approval, and reporting fields;
- contains no Oracle-specific contract in the new primary API or panel;
- uses the US Signal logo and responsive module-standard presentation;
- installs one panel in each intended module and remains idempotent;
- does not rewrite `App.jsx` or `main.jsx`;
- registers the API map exactly once; and
- declares that no migration or deployment action is part of the package.
