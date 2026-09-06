# Executable enterprise evidence coverage

This change extends the post-PR-864 internal-first answer path. An entry in the universal capability catalog alone is not evidence that an adapter runs. The executable mapping is `CelarAiEnterpriseEvidenceCatalog`; the answer pipeline selects those reads before diagnostic/procedure sources and records each result.

| Question family | Executed source | Scope and limits |
| --- | --- | --- |
| Customer abbreviation, AE/sales representative, project ownership | Existing deterministic customer/project resolver | Exact stored customer name/code; authorized projects; ambiguity and unassigned roles remain explicit. Project ownership is not inferred to be customer-wide ownership. |
| People, teams, manager and team lead | Parameterized `EnterprisePeopleSql` | Existing self/project/team/reporting/portfolio authority; active identities and current relationship dates. More than 200 relationship rows is incomplete, not a complete directory. Conflicting current relationships remain visible. |
| Person project/task counts | Existing deterministic internal resolver | Exact authorized identity and verified aliases; distinct current assignments and SQL counts. Group questions now reach the enterprise planner instead of single-person resolution. |
| Projects, assignments, staffing requests | Module 019 project workspace overview | Owning role, assignment and effective-user scope. Source row caps and response budgets are enforced. |
| My recorded hours over dates | Parameterized `EnterpriseOwnTimeSql` | `TIME_VIEW`, effective user only, read-only transaction. Exact ISO dates/ranges or this/last week/month/quarter/year, today/yesterday, maximum 366 days. SQL totals separate approval status and billability; user timezone controls calendar boundaries. |
| Weekly task lines and assigned work | Module 001 work queue and weekly lines | Effective user and explicit Sunday week start. Does not call the timesheet GET that auto-submits holidays. |
| Approvals and corrections | Module 002 manager approvals | Owning approver/manager/finance scope, explicit week. Arbitrary multiweek approval history is not represented as a current-week answer. |
| Capacity and utilization | Module 070 forecast | Owning access and deterministic forecast formulas; explicit reported 14-week horizon. |
| Project financials | Project Financial Truth portfolio | Owning financial/project scope and calculation definitions; source failures and capped portfolios are incomplete. |
| Contracts and prepaid balances | Module 060 contracts overview | Owning commercial read authority. This supplies stored contracts and balances, not every quote/rate-card version. |
| Billing and expenses | Module 039 billing candidates | Owning billing/project scope; approved lines, invoice history, rate-resolution state and billing blockers. A candidate list is not a full accounting ledger. |
| Commercial pipeline | Module 063 opportunities | Owning module read authority; actual recorded opportunity state. |
| Risk totals and mitigation | Module 082 summary and risks | Owning project scope; deterministic totals and actual risk records. Capped detail is incomplete. |
| Audit/change history | Canonical audit events | Owning audit permission; recent 100-event bound, never claimed as exhaustive history. |
| Documents and mixed DB/document questions | Existing private Help RAG plus server-produced structured evidence | Document authorization, current version, revocation and citation checks remain in the existing retrieval path. API references are separate from document citation IDs. No structured row can satisfy missing document evidence. |
| Diagnostics, release, observability and procedures | Existing system knowledge tool executor | Existing permissions and source contracts remain authoritative. |
| Clearly public questions | Existing Module 064 route | No private records, retrieved documents, identifiers or tool bodies are sent externally. |

## Evidence semantics

- HTTP failure bodies do not enter synthesis or summaries. Forbidden, failed, malformed, degraded, paginated, capped and budget-omitted evidence is unknown, never a fabricated zero.
- Genuine empty successful time queries return zero from SQL. Recorded hours include draft and other statuses unless the answer explicitly selects the reported status breakdown.
- Complete structured entries, provenance, scope and observation time are supplied to private RAG within a bounded budget. Omitting a required entry makes the combined answer partial. Retrieved content is data, not instructions.
- Browser/session and View-As headers reach owning APIs. New DB reads require an active matching effective-user scope, use server-owned SQL and typed date/user parameters, and run in a read-only transaction.
- Runtime configuration, Module 064 provider order, Oracle DNS address policy, deployment controllers and production are outside this change. Module 025 and FlowHive document-authority repair remains separate.

## Validation

`CelarAiEnterpriseRetrievalTests` compiles against the API and checks selection, public isolation, team routing, calendar/leap-day boundaries, scope forwarding, authorization denial, response budgets, malformed/degraded/paginated evidence, known source caps and combined provenance. With `CELAR_AI_TEST_CONNECTION_STRING`, it executes the actual SQL against the existing PostgreSQL regression fixture to verify dates, user separation, SQL totals and directory scope. The enterprise retrieval CI runs both modes plus the existing internal-data/privacy suite.

## Remaining source limitations

This provides broader executable coverage, not unrestricted database access or a guarantee that every question has a recorded answer. It does not invent customer-wide owners, old reporting relationships, cross-person historical time access, complete accounting ledgers, or unimplemented module adapters. The runtime must report these gaps and request the appropriate context/source. Large portfolios that reach existing endpoint caps require an owning-module pagination/filtering change before exhaustive cross-domain answers can be certified.
