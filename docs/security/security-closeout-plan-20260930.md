# Security closeout plan: preserve access and validate each finding

This PR begins container packaging and a closure framework. It does not close findings, deploy services, replace Oracle or alter the local Super Administrator. The register retains 149 original findings plus A001/A002. Pending evidence does not imply earlier source fixes are absent.

## Local account preservation

Keep the existing user identity, enabled local sign-in and legitimate Super Administrator role. No password reset, rename, Entra-only conversion or account removal. Use isolated accounts for destructive tests, and non-destructive real-account access checks before and after an authorized release. Application authority remains separate from the API's restricted database identity.

The first identity PR should test local login without Oracle/Entra, approved recovery, last usable local administrator protection, delegated/ordinary administrator restrictions, synchronization and migration replay, disabled/revoked roles, View-As restrictions and old-session invalidation after an intentional rotation. Never publish real credentials or operate destructive fixtures on the user's account.

## Workstreams, each implemented and reviewed in PRs

| ID | Scope | Required proof |
|---|---|---|
| W01 | Identity, local Super Administrator and recovery | Legitimate login/recovery, blocked takeover, no accidental lockout, session lifecycle |
| W02 | Critical release/backup paths and host isolation | Safe invalid-input rejection, applicable host ownership/race checks, controlled restore |
| W03 | Roles, project ownership and document visibility | Authorized success plus foreign/delegated/expired-assignment denial |
| W04 | Uploads, scanning, OCR, bounded parsing and exports | Scan/version linkage, ordinary inputs, bounded rejection, safe downloads |
| W05 | Time, approval, billing and concurrency | Final-state preservation, stale-evidence rejection and correct actual actor |
| W06 | Microsoft/CRM integration credentials | Valid consent/callback/rotation, destinations, refresh races and revocation |
| W07 | Telemetry, historical disclosure and provenance | Compatible authenticated ingestion, exposure disposition and exact-image evidence |

Reproduce missing cases before changing a recorded source repair. Validate legitimate reports and replacements as well as denied requests. Host evidence must come from the relevant installation, not unrelated Azure API UAT. Owner/approver assignment remains an explicit project task rather than an invented person.

## Proposed sequence

Container foundation -> local-account and finding-specific tests -> off-by-default document adapters and separate policies -> managed-container placement in the existing Pulse environment -> protected Test cutover and recovery -> independent Foundry evaluation -> retirement of old services -> per-finding security review. Production approval stays separate. No new Azure VM is required.

## Acceptance and closure

Document cutover needs official signature bootstrap/reload, native clean and harmless antivirus fixtures, ordinary imports, missing/stale scanner behavior, bounded malformed-input rejection, worker restart, durable retries and an isolated Oracle-unavailable test. The current default must remain until these pass. Remove old services only after a verified rollback path; never make scanner failure mean clean.

Each finding requires original ID, remediation PR(s), merged commit, installed environment/image, positive and negative receipts, residual applicability and security disposition. State-transition cases also need stored-state and actual-actor audit evidence. Repeat affected tests when the installed source changes; keep previous evidence as history.

The register deliberately starts without fabricated evidence. Its validator rejects missing/duplicate original IDs and unsupported validation or closure. The initial-PR test expects all rows pending; update that assertion in a later reviewed evidence PR when genuine validations are attached.

Report template: Finding [ID] remediated and validated in [environment] through PR [number], installed as [SHA/image]. Positive and negative cases [receipts] passed. Local Super Administrator access preserved. Remaining Production applicability and security retest/disposition: [state].

Historical disclosure needs containment and credential/exposure disposition; a new publication fix cannot erase downloaded historical copies. Unused paths need documented non-applicability/decommissioning rather than an unsupported fixed claim. A successful Test release does not attest all host or Production installations.

C001-C005 track exact deployed-image assessments, stale source markers, current-SHA installed security tests, consistent closure records and organization-mirror equivalence. Organizational SAML restrictions remain intact.

Application Insights DisableLocalAuth controls telemetry ingestion, not Pulse login. Inventory SDK consumers, establish a compatible authenticated path and verify telemetry continuity before a separate setting change. Do not disable the user's local account to address telemetry.
