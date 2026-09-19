# Configuration and data inventory

Owner roles below are proposed responsibility assignments, not evidence of approval or completion. Maintain an explicit export/import allowlist; never copy the entire Test database as production initialization. Review relationships and identifiers when selectively transferring configuration.

| Area | Production treatment | Owner | Evidence required |
|---|---|---|---|
| Schema, functions, migration ledger | Build from reviewed dependency-aware initialization; record only executed changes | Engineering / DBA | Fresh install succeeds twice from empty environments; schema comparison |
| Module catalog, roles, permissions and scoped policies | Retain approved definitions; review role grants separately | Platform administrator / security | Role matrix, negative access tests and effective-user preview tests |
| Employee accounts, managers, teams | Reimport approved employees from Entra; map stable identities and verify management relationships | Identity administrator / team managers | SSO, offboarding, manager/PM routing and bootstrap access verification |
| Break-glass/local accounts | Provision explicitly with production credentials; exclude development personas | Security / platform administrator | Recovery login tested and ownership recorded; no exported password hashes |
| Customers and contacts | Import from approved authoritative sources; reconcile duplicates and external IDs | Sales operations | Source approval, reconciliation counts, no test customers |
| Templates, branding, document numbering | Retain reviewed templates and settings; initialize numbering by business decision | Sales operations / PMO | Real format examples for SOW/GSD and numbering decision |
| Rate cards, contract types, utilization rules, calendars, time categories, locations | Review and retain only approved defaults | Finance / engineering / PMO | Policy sign-off; effective dates and time zones tested |
| Approval workflows, notification rules | Recreate approved rules; empty delivery history and queues | PMO / operations | Engineer → Manager → PM/PTC routing tests and recipient verification |
| Module 065 / Entra SSO and sync | Production app registration, redirect URLs, secret references and tenant policy | Identity administrator | Correct audience/redirects, scoped sync, expiring-secret alert |
| Module 067 / email | Production sender and recipient controls; enable only after testing | Messaging administrator | Actual delivery, duplicate suppression, retry and queued-message review |
| Module 026 / ConnectWise SELL | Production connection and secret references; fresh sync checkpoints | Integration owner / Sales operations | Account/environment identity, dry-run mappings, idempotency and authorized write test |
| Module 064 / AI routing and Celar | Approved providers, models, privacy settings and production-specific secret references | AI/platform owner | Provider fallback and access-scoped internal-first answers |
| Database, document storage, queues and secrets | Separate production resources or demonstrably isolated logical namespaces | Cloud operations / security | Live resource IDs, permissions and isolation evidence; backup/restore proof |
| RAG indexes, OCR output, caches and embedding collections | Build from approved production sources; exclude UAT corpus | AI/platform owner | Retrieval canary tests prove test records cannot be returned and ACL changes propagate |
| Monitoring, diagnostics, audit retention | Establish production-specific destinations and retention policy | Operations / security | Tested alerts, access restrictions, cost estimates and retention ownership |
| Projects, tasks, service requests, assignments, timesheets, approvals, expenses and invoices | Start empty; import real opening work only by a separate reviewed business-data plan | PMO / Finance | Before/after reconciliation and workflow acceptance |
| Generated SOW/GSD versions, attachments, SELL submissions, signed artifacts | Start empty unless explicitly approved real opening records are imported with evidence | Sales operations / records owner | Database/file association and download verification |
| Test email outboxes, integration jobs, retries, sync cursors and scheduled jobs | Do not transfer; initialize clean state before enabling workers | Integration / operations | No replay of test notifications or external writes |
| Test audit/event evidence and backups | Retain in Test/archive under an agreed retention plan; never relabel as production history | Security / operations | Archive/recovery evidence and production log boundaries |

These are logical data domains, not SQL deletion instructions. Exact tables, dependency relationships, object paths and secret references must be inventoried during implementation. Some records are intentionally append-only; a clean database avoids bypassing those protections.
