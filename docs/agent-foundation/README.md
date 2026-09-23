# Shared Celar Agent Runtime, Role-Scoped Assistance and Handoffs

## Status and release scope

This is the first, additive **foundation PR**, based on main `4c31007053359b1cf41681d686742f6c08fbb3b6`.
It contains executable read-and-propose runtime code, a Module 064 model bridge,
capability metadata, a reusable frontend component, synthetic regression tests
and a staged rollout contract. It is not the complete platform-wide deployment.

**Not wired or enabled:** no HTTP endpoints, DI registration, background worker,
production run-store adapter, owning-module authorization/tool adapters, live
workspace mount, database migration, notification delivery or approval action is
introduced here. Setting a flag alone cannot enable a live workflow. No in-memory
production fallback is supplied. Keep this PR draft until its checked scope is
reviewed; implement and qualify the remaining host adapters before a UAT pilot.

The separation is deliberate: first establish and test the common boundary;
then connect it to real modules without inventing permissions or persistence.
Do not call catalog entries implemented business integrations.

## Included code

- `Agents/AgentContracts.cs`: server-only identities/resources, typed decisions,
  immutable checkpoints and proposals, authority/storage/tool adapter contracts,
  finite limits, disabled defaults and deny-all unbound authority.
- `AgentCapabilityCatalog.cs`: 15 business-capability bundles covering Engineering,
  leads/managers, PMs and PM leadership, coordinators, PTC, both SA teams and their
  managers, Sales/AE, Inside Sales, Finance/Accounting/Billing, executives,
  administration/operations and security/audit. Audience labels grant no rights.
- `AgentKernel.cs`: one bounded model decision per advance, atomic checkpoint
  reservation, source/permission rechecks, read-tool allowlists, repeat protection,
  clarification pause/resume, draft handoffs and interruption handling.
- `Module064AgentDecisionSource.cs`: control decisions call the existing
  `HelpAssistant` capability through `CelarAiCapabilityRouter`. No agent-owned
  provider list, model key, SDK or direct inference URL.
- `AgentDecisionParser.cs`: strict JSON, bounded sizes, no duplicate or unknown
  properties, and no model-supplied recipient IDs, URLs or business action payloads.
- `src/agents/WorkAssistantPanel.jsx`: unmounted shared presentation component;
  displays only the current server-authorized session/resource projection, ignores
  browser role labels, aborts stale requests, and labels outcomes as proposals.

Tests inject a synthetic model, authority, storage and read tools. They are not
proof of live permissions, durable PostgreSQL persistence, model quality or browser
integration. CI compiles the actual API and frontend, not only source assertions.

## Architecture and ownership

The runtime belongs in the Pulse backend, not on the Oracle inference host. It
coordinates existing services rather than replacing their business rules.

```
Workspace or approved event
  -> authenticated host + current record authorization
  -> AgentKernel (bounded goal and checkpoint)
  -> Module 064 (every model call)
  -> approved read tool / proposal
  -> owning module and verified source evidence
  -> explicit human review / separately authorized next-owner handoff
```

Module 064 remains model-order and disclosure authority. Role/record/team/field
access remains with existing backend authorization and the owning modules. Agent
Operations will configure playbooks and triggers, not duplicate provider settings,
user roles, reporting relationships or commercial approvals.

The role journeys are learning content, not executable authorization. Review each
input/action/output/acceptance/receiving-role/exception with its business owner,
then implement the corresponding server-side contract. Never execute the frontend
playbook text as instructions with privileged credentials.

## Role and workspace rollout map

| Experience | Initial capability | Scope authority and business boundary |
|---|---|---|
| Engineer workspace 019 | Assignment readiness, document lookup, blocker proposal | Assigned work only; own actual time; no approval or project closeout. |
| Engineering Lead | Technical readiness and review | Authorized functional team; no implied manager approval. |
| Engineering / People Manager | Capacity and staffing proposals | Explicit reporting/team scopes; no unrestricted Engineering grant. |
| PM workspace 018 / FlowHive | Handoff readiness, delivery assessment | Assigned projects; no AI baseline adoption or customer commitment. |
| PM Lead / Manager | Portfolio review | Assigned PM teams/portfolio; other projects stay inaccessible. |
| Project Coordinator | Delegated follow-ups | Assigned coordination only; not PTC time-steward powers. |
| Project Team Coordinator | Time exception proposal | Existing correction/reallocation authority; no worker impersonation or invented hours. |
| SOW/GSD 025, System SA | Scope review and subsequent domain generation | Assigned engagements; System SA team/reviewer boundaries. |
| SOW/GSD 025, Collaboration & Networking SA | Same capability | Separate team/manager/engagement scopes, not shared access. |
| SA managers | Workload and aging drafts | Explicit managed SA teams; reassignment/confirmation need their own grants. |
| Sales / Account Executive | Intake and handoff completeness | Assigned customer/opportunity; no technical approval or promised capacity. |
| Inside Sales / Resale | Commercial supporting records | Existing commercial support grants; verify external receipts. |
| Finance / Accounting / Billing | Financial readiness explanations | Separate function and field permissions; no rates, invoice, payment or approval writes. |
| Executive | Business indicator review | Authorized aggregate evidence; title does not grant operational approvals. |
| Administrator / Super Administrator / Operations | Operational review | Own session, correct environment, existing governance; no agent superuser credential. |
| Security / Audit | Evidence review | Map actual configured roles before enablement; audit-only remains read-only. |

These labels must be bound to the **actual** role catalog and dynamic policy during
integration. Do not alias Engineering Manager to Lead, Coordinator to PTC, or Finance
to unrestricted Billing authority in this layer. Users with multiple roles keep
explicit denies and separation of duties; changing a UI working context grants nothing.

## Runtime and persistence contract

Only `read`, `ask`, `handoff` (proposal) and `finish` (proposal) are accepted.
No write/publish/deploy/approve tool exists in this initial runtime.

`StartAsync` requires explicit Test enablement and a verified own-session actor.
`AdvanceAsync` reauthorizes, atomically reserves one model call, runs one permitted
decision within a deadline, rechecks source/authority and checkpoints the result.
A second writer cannot advance the same revision. Source changes stop a run.
Clarification preserves consumed budgets and the pinned authoritative resource.
A crash after reservation leaves `Running`; explicit recovery records an unknown
interrupted outcome without automatically replaying inference or resetting limits.

The production `IAgentRunStore` must filter actual/effective owner before returning
any content, compare-and-swap revision atomically, keep owner/resource/capability and
deadline immutable, and write its audit event in the same transaction. Use a reviewed
PostgreSQL migration plus a lease/worker implementation. Integration must support
idempotent start keys, cancellation while waiting/running, bounded retention, and
restart recovery; those host/storage features are **not implemented by this PR**.

Never serialize `AgentRun`, its goal/evidence, or server actor/resource contracts
directly as an HTTP request/response. A qualified host produces a freshly authorized
minimal projection for `WorkAssistantPanel`, including an opaque context key that
changes on identity, role, record and source-version changes. No cookies or provider
keys belong in run storage. PostgreSQL, owning modules and approvals remain sources
of truth; a generated assumption never becomes a customer fact by reuse.

## Model routing and disclosure

Control decisions deliberately use Module 064 `help_assistant`. Future generation
tools delegate to the existing SOW/GSD or FlowHive engine and its respective Module
064 capability. They must not instantiate another provider router or own an order.

Control-decision evidence is conservatively private: full Service Scope approval
is not permission to disclose other retrieved customer, workforce or financial
records. Thus a cloud provider may be ineligible even when listed first. Record the
actual router decisions, never reorder the list or invent a fallback success.
Qualifying any external control-decision payload requires a separate explicit,
purpose-specific disclosure contract. Preserve the current Service Scope-only
payload and consent when integrating the SOW generation tool.

A refusal stops. Local templates do not qualify as model decisions. All tool names
and possible handoff types are registered server-side; the model chooses among
those names but cannot supply identity, recipient, URLs, SQL or authorization.

## Cross-role handoff contract

The initial happy-path test is an assigned engineer reading assignment evidence and
preparing an `engineering_to_pm` proposal. The resolver must obtain the current PM
from the actual project record and validate that recipient's own access. The sender's
identity is not forwarded as the recipient's authority.

The proposal binds run, owner, exact source/resource version, destination role/user
and text with SHA-256. This is **not an approval credential**. `Applied` and
`NotificationSent` remain false. There is no send/accept/approve handler in this PR.
A later receiving-owner review must reload the current assignments, source and
permissions. Lost ownership or revised scope invalidates the prior proposal.

Future approved actions must use existing services and an outbox, retain receipts,
deduplicate events and reconcile unknown external outcomes. Never infer that a
queued SELL job was published, a downloaded invoice was posted, or a proposed task
was approved. Do not introduce a new manager-approval step into the existing PTC
administrative reallocation flow.

## Staged rollout

1. Review/validate this additive foundation; no live activation or migration.
2. Implement Test-only host endpoints, real scoped authorization/read adapters,
   PostgreSQL persistence/leases/outbox, cancellation, run projections and feature
   toggles. Attach the shared component to 019 and 018 without broad role grants.
3. Verify Engineering -> PM blocker proposal/review with separate identities and
   real source versions. Add the SA -> PM approved-document handoff using current
   SOW/GSD generation and five-phase validation, not a new generator.
4. Extend to managed capacity, coordinator/PTC, commercial and financial readiness
   after their specific business owners approve mappings and data fields.
5. Introduce narrowly approved actions only after independent approval, recipient
   authorization, idempotency and uncertain-outcome reconciliation tests pass.

Do not activate or route around failing live generation just because this kernel's
synthetic tests pass. Keep build success, installation, authorized workflow execution
and genuine model acceptance as four separately evidenced outcomes.

See `rollout-checklist.md` for exact gates. No merge, deployment, production access,
provider-setting change, consent change, role grant or notification is requested by
this preparation PR. The concurrent billing/closeout work (PR1151) is not modified.
