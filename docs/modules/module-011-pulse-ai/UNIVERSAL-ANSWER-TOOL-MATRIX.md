# Ask Celar AI — Universal Answer Tool Matrix

## Purpose

This document records the governed evidence capabilities that Ask Celar AI may request. A catalog entry does not grant access and does not prove that every adapter is active. It identifies the owning module, authority, access boundary, freshness class, citation requirement, and current implementation state so that Celar AI cannot represent a planned source as operational.

## Availability states

| State | Meaning |
|---|---|
| `available_existing_adapter` | The current Pulse codebase already contains a governed read or orchestration path that can supply this evidence family. Live UAT is still required. |
| `available_oracle_runtime` | The authenticated Oracle Celar AI runtime exposes this private service behind HTTPS while its internal port remains loopback-only. |
| `available_protected_test` | The capability is deployment-managed for protected Test and must not be represented as Production. |
| `available_only_when_module064_route_is_ready` | The source is usable only when Module 064 has an approved route, protected credentials, fresh health, and the privacy policy permits the request. |
| `cataloged_requires_execution_adapter` | The business authority exists, but a dedicated least-privilege Ask Celar AI read adapter still must be implemented and validated. The planner may identify the need but may not fabricate the result. |

## Tool contract

Every tool entry contains:

- stable code;
- display name;
- domain;
- owning modules;
- authoritative source;
- current availability;
- access policy;
- freshness class;
- deterministic flag;
- citation requirement;
- private-only flag;
- mutation prohibition;
- query signals;
- required source types; and
- logical or physical route.

Every tool in this package has `MutationAllowed=false`.

## Identity and permissions

| Code | Owning modules | Availability | Authority and intended use |
|---|---|---|---|
| `effective_identity` | 009, 010, 012, 037, 059, 062 | Existing | Resolve actual user, effective View-As user, active status, role, and session identity. |
| `role_permission_evidence` | 012, 037, 079, 997 | Existing | Explain module access, record permissions, and policy version without widening scope. |
| `people_directory` | 009, 010, 018, 062 | Existing | Resolve active people and verified aliases only inside authorized organization scope. |
| `team_scope` | 003, 018, 037, 057, 062 | Existing | Provide current reporting, team, and explicit scope membership. |
| `reporting_relationships` | 003, 018, 062 | Adapter required | Resolve effective-dated manager and team-lead relationships through an owning-module adapter. |

### Required next adapters

- effective-dated reporting relationship reader;
- sanitized module-permission explanation reader;
- identity ambiguity response that returns multiple candidates without exposing unauthorized profiles.

## Projects and delivery

| Code | Owning modules | Availability | Authority and intended use |
|---|---|---|---|
| `project_portfolio` | 018, 019, 020, 055C, 055D | Existing | Current authorized project lifecycle, customer, PM, status, and portfolio evidence. |
| `project_assignments` | 001, 018, 019, 055C | Existing | Effective project assignments and Work Register assignment authority. |
| `task_assignments` | 001, 001A, 019, 055C | Existing | Active task, assigned identity, hours, status, and closeout evidence. |
| `resource_requests` | 018, 019, 055C | Adapter required | Unfilled engineering-resource demand and assignment status. |

### Required next adapters

- resource-request inventory with requested role, dates, status, and assignment;
- project-role lookup that distinguishes PM, Engineer, Lead, and request-only relationships;
- project lifecycle reconciliation for active versus closed, completed, cancelled, and archived work;
- project assignment conflict detection across historical and canonical sources.

## Time, approval, and capacity

| Code | Owning modules | Availability | Authority and intended use |
|---|---|---|---|
| `timesheet_status` | 001, 002, 003, 007, 023, 028 | Adapter required | Time periods, entries, hours, submission, correction, mobile, after-hours, and work-log evidence. |
| `approval_status` | 002, 007, 023, 028 | Adapter required | Current approval stage, approver, decision, reason, and correction state. |
| `capacity_utilization` | 003, 018, 057, 069, 070 | Existing | Approved capacity, assignments, time, leave, pipeline demand, and utilization formula. |

### Required next adapters

- self, manager, PM, PTC, and administrator timesheet-status readers;
- approval queue and decision-history reader;
- utilization calculation evidence including target, numerator, denominator, excluded time, period, and time zone;
- capacity forecast that preserves unknown leave, assignment, and pipeline values instead of treating them as zero.

## Financial and commercial

| Code | Owning modules | Availability | Authority and intended use |
|---|---|---|---|
| `project_financial_truth` | 005, 018, 019, 022, 030, 036, 038, 039, 042, 055B, 060, 063 | Existing | Current contract, rate, time, expense, billing, cost, revenue, margin, variance, and source-health evidence. |
| `expense_billing` | 005, 022, 038, 039, 042 | Adapter required | Expense certification, billing candidate, invoice, and reconciliation status. |
| `commercial_contracts` | 021, 024, 025, 026, 036, 055B, 060, 063, 073, 074 | Adapter required | Approved customer contracts, rates, block-of-hours, quote, and commercial assumptions. |
| `commercial_pipeline` | 021, 024, 026, 063 | Adapter required | Opportunity stage, expected delivery date, customer, and expected effort. |

### Financial reliability requirements

- every answer identifies contract and rate authority;
- every monetary answer identifies currency and period;
- every calculation distinguishes planned, actual, forecast, billed, collected, and unknown values;
- optional-source failure must not erase healthy authoritative values;
- unavailable values remain unavailable;
- no estimate is represented as an actual;
- a public provider never receives private financial records.

## Documents and retrieval

| Code | Owning modules | Availability | Authority and intended use |
|---|---|---|---|
| `project_documents` | 011, 019, 020, 025, 055C, 055D, 066 | Existing | Authorized document inventory, category, version authority, checksum, processing state, and project scope. |
| `malware_scan` | 011, 055C, 055D, 079 | Oracle runtime | Authenticated ClamAV result tied to immutable source checksum and signature version. |
| `document_extraction` | 011, 019, 055C, 055D | Existing | Private native extraction with page, slide, worksheet, heading, and source anchors. |
| `ocr` | 011, 019, 055C, 055D | Oracle runtime | Tesseract 5 OCR for image-only pages through authenticated private extraction. |
| `private_retrieval` | 011, 019, 020, 055C, 055D, 066 | Existing | Current permission-filtered chunks, ranking, citations, version authority, and revocation. |
| `conversation_attachments` | 011 | Existing | Owner-scoped durable attachments; View-As writes remain blocked. |

### Document reliability requirements

- scan before extraction;
- source signature and size validation;
- no macro-enabled or executable content;
- extraction and OCR bounded by immutable checksum;
- authority determined by approval or supersession evidence, not upload time alone;
- re-authorization at retrieval time;
- raw chunks and vectors excluded from the browser response;
- external provider receives no private source text;
- revocation removes retrieval eligibility without retraining.

## Planning

| Code | Owning modules | Availability | Authority and intended use |
|---|---|---|---|
| `flowhive_plan` | 019, 020, 057, 066 | Existing | Cited task plan, dependency model, deterministic schedule, critical path, and immutable review versions. |
| `project_forge` | 019, 033, 055C | Existing | Cited estimate, assumptions, dependencies, hours, dates, and workbook review state. |

### Planning reliability requirements

- approved SOW scope is the primary private authority;
- every generated task or estimate retains citations;
- generic uncited plans are rejected where SOW evidence is required;
- schedule calculations use deterministic engines;
- capacity, holiday, leave, and customer constraints remain explicit limitations until applied;
- generated output remains a draft until human review, approval, and baseline.

## Risk and governance

| Code | Owning modules | Availability | Authority and intended use |
|---|---|---|---|
| `risk_register` | 019, 082 | Existing | Immutable risk versions, probability, impact, residual score, mitigation, action owner, and project scope. |
| `audit_history` | 008, 012, 037, 079, 997, 998 | Adapter required | Actor, actual/effective identity, action, entity, timestamp, and policy version. |
| `data_governance` | 079, 997 | Existing | Classification, retention, revocation, lifecycle, and policy evidence. |
| `security_posture` | 064, 077, 079, 997, 998 | Existing | Sanitized control, transport, secret-reference, and private-boundary evidence. |

### Required next adapters

- permission-scoped cross-module audit reader;
- authoritative acknowledgment and approval-history reader;
- retention and deletion explanation by entity type;
- risk-to-deliverable cross-domain join with current citations.

## Operations and diagnostics

| Code | Owning modules | Availability | Authority and intended use |
|---|---|---|---|
| `live_api_inventory` | 011, 058, 076, 078, 998 | Existing | Current ASP.NET endpoint inventory, route, method, module, safety, and release SHA. |
| `system_diagnostics` | 013, 016, 075, 076, 078, 998 | Existing | Allowlisted health, dependency, diagnostic code, correlation, and safe-retest evidence. |
| `release_deployment` | 058, 075, 076, 077, 078, 083 | Existing | Exact release commit, image digest, environment, run, verification, and rollback evidence. |
| `observability` | 014, 015, 017, 078, 998 | Adapter required | SLO, backup, recovery, replication, monitoring, and dependency health. |
| `defect_tracker` | 013, 016, 076, 998 | Existing | Defect status, module, impact, owner, resolution, and verification evidence. |

### Diagnostic reliability requirements

- current environment evidence is required;
- source code is not live-environment proof;
- observed fact, hypothesis, and recommendation are distinct;
- correlation IDs and diagnostic codes are sanitized;
- failed, forbidden, and unavailable tools remain visible as failed evidence rather than disappearing;
- no safe retest may invoke a mutation.

## AI runtime and provider controls

| Code | Owning modules | Availability | Authority and intended use |
|---|---|---|---|
| `provider_configuration` | 011, 064, 079 | Existing | Persisted capability route, target order, protected secret references, fresh probes, and sanitized readiness. |
| `oracle_runtime_readiness` | 011, 064 | Protected Test | Authenticated Oracle HTTPS readiness for generation, embeddings, OCR, ClamAV, and no raw-document logging. |

### Runtime boundaries

- Module 064 remains the one provider authority;
- the five Oracle endpoints are deployment-managed Test settings;
- bearer value is never returned;
- public TCP 443 terminates at Caddy;
- Python gateway, Ollama, and ClamAV remain loopback-only;
- private data is never sent to Claude or OpenAI;
- external fallback is limited to sanitized public or generic guidance under the configured route;
- a provider safety refusal is terminal.

## Product and public knowledge

| Code | Owning modules | Availability | Authority and intended use |
|---|---|---|---|
| `product_knowledge` | 011, 029, 076, 999 | Existing | Source-controlled module guidance, navigation, current procedures, and safeguards. |
| `governed_public_information` | 011, 064 | Module 064 dependent | Approved current or stable public sources with no private Pulse payload. |

### Public route rules

- named internal people, customers, organizations, projects, and records remain private by default;
- clearly public current facts require retrieval-time evidence;
- public stable explanations use only approved public content;
- no internal tool result or private user question is appended to the public request;
- an external answer without a current source cannot establish a changing fact.

## Adapter backlog order

### Priority 1 — high-frequency internal facts

1. timesheet status and period calculations;
2. approval queue and decision history;
3. reporting relationships and team scope;
4. resource request status;
5. expense and billing readiness;
6. commercial contract and rate evidence.

### Priority 2 — operational completeness

7. cross-module audit history;
8. observability, backup, and recovery health;
9. opportunity and pipeline evidence;
10. data-retention explanation by entity;
11. current notification and acknowledgment state;
12. normalized defect-to-release correlation.

### Priority 3 — retrieval quality improvements

13. representative document-format test matrix;
14. reviewed hybrid lexical/vector retrieval decision;
15. reranking benchmark if top-result quality is insufficient;
16. extraction expansion only where measured gaps exist;
17. caching only where measured latency justifies it.

## Adapter implementation standard

Every new execution adapter must:

- live with or call the owning module;
- use parameterized, pre-reviewed queries or existing module APIs;
- accept actual and effective identity;
- apply module, project, team, reporting, assignment, document, and record scope;
- identify source version and retrieval time;
- return structured evidence rather than a prose guess;
- preserve null and unknown values;
- include deterministic calculation metadata when applicable;
- expose a sanitized diagnostic code on failure;
- never return a secret, raw document chunk, vector, storage path, or unrestricted data dump;
- never mutate state through Ask Celar AI;
- fail closed on ambiguous identity, mixed authorization, stale evidence, timeout, or partial source failure;
- include behavioral and permission tests; and
- be registered in this catalog only after its owner and scope are documented.

## Honest readiness language

The UI and API must use these meanings:

- **Cataloged** — the required evidence family and owner are known.
- **Source ready** — code and tests for the reliability contract exist.
- **Adapter ready** — a governed read-only implementation exists.
- **Test verified** — protected Test proved behavior with representative data and permissions.
- **Production ready** — separately authorized Production controls, operations, monitoring, rollback, and evidence are complete.

No diagram or UI may convert a cataloged or source-ready tool into a green deployed component without the corresponding runtime evidence.
