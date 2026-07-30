# Pulse AI System Intelligence, Live API Discovery, and Troubleshooting

## Document control

| Field | Value |
|---|---|
| Owner | US Signal |
| Product | Pulse |
| Module | 011 — Pulse AI |
| Phase | 011E — System Intelligence and Troubleshooting |
| Contract | `pulse-ai-system-intelligence-v1-20260730` |
| Migration | `054_pulse_ai_system_intelligence_conversations` |
| Classification | US Signal Internal — Confidential |
| Source status | Implemented on an isolated branch; not merged, migrated, activated, or deployed by this package |

## Mission

Pulse AI is the permission-aware intelligence layer for Pulse. This phase makes it useful as an operational advantage rather than a page that returns only a future execution plan.

Pulse AI can now answer comprehensive questions about:

- every authorized Pulse module and workflow;
- the APIs registered in the running application revision;
- API ownership, methods, route patterns, session boundaries, and safe-retest eligibility;
- current platform, database, identity, integration, worker, release, deployment, observability, security, and diagnostic evidence;
- projects, private documents, Timesheet behavior, FlowHive, reports, financials, and business processes where authorized sources exist;
- errors, timeouts, HTTP 401/403/404/5xx behavior, missing routes, dependency failures, and correlation evidence;
- current architecture, trust boundaries, integrations, dependencies, and source-of-truth ownership; and
- future enhancements, including architecture, APIs, migrations, permissions, security, operations, tests, rollout, rollback, risks, and acceptance criteria.

The service must answer the question directly. It must not return only a plan that says multi-tool execution is not enabled.

## Core architecture

```text
Authenticated Pulse user
        |
        v
Actual/effective identity + Module 011 permission
        |
        +----------------------------+
        |                            |
        v                            v
Live ASP.NET endpoint registry   Intent and scope classification
        |                            |
        +-------------+--------------+
                      |
                      v
       Source-controlled same-origin GET tools
       - Module 013 platform/API inventory
       - Module 016 operational evidence
       - Module 068 system architecture
       - Module 076 defects
       - Module 077 releases/deployments
       - Module 078 observability/SLO/alerts
       - Module 998 diagnostics/issues
       - Module 064 sanitized AI readiness
       - Module 011 private RAG/document readiness
       - authorized reporting/financial sources
                      |
                      v
     Deterministic current-state answer and diagnostics
                      |
           +----------+----------+
           |                     |
           v                     v
Private Pulse model         Deterministic answer
when configured             when model unavailable
           |                     |
           +----------+----------+
                      |
                      v
Comprehensive cited answer + API findings + troubleshooting
+ future enhancement blueprint + durable conversation history
```

## Live API discovery

Pulse AI does not rely on a manually maintained API spreadsheet to answer which APIs are running.

`PulseAiSystemApiCatalogService` reads the current ASP.NET `EndpointDataSource` during the request. It captures each registered route and HTTP method, including:

- stable API identifier;
- HTTP method;
- route pattern;
- endpoint display and endpoint name metadata;
- inferred module number and name;
- purpose;
- route order;
- parameterized-route evidence;
- session or anonymous boundary;
- explicit authorization metadata where present;
- safe-retest support and reason;
- registration state; and
- running release SHA or revision marker.

The API inventory distinguishes three concepts:

1. **Registered** — the running ASP.NET application has the route/method definition.
2. **Reachable and authorized** — a request using the current effective identity can reach the owning endpoint.
3. **Healthy** — the endpoint and its required dependencies return a successful, semantically valid result.

A registered API does not prove that its database, integration, record scope, or downstream dependency is healthy.

## Safe API retest

Pulse AI can perform an explicitly confirmed same-origin retest only when all of the following are true:

- the route is registered in the running application;
- the method is `GET`;
- the route has no path parameter;
- the route is not an authentication, callback, token, secret, logout, download, export, attachment, stream, refresh, probe, or recursive diagnostic route;
- the actual user has `RETEST_PULSE_AI_SAFE_API`;
- Administrator View-As is not active; and
- the exact confirmation `RETEST-PULSE-AI-SAFE-API` is supplied.

The retest returns status, latency, diagnostic code, module owner, and observation time. It does not return the endpoint response body and does not change application state.

## Governed troubleshooting

Troubleshooting answers use source-controlled tools. A user or model cannot provide an arbitrary URL.

The default evidence chain is:

1. Confirm the user, View-As state, environment, timestamp, route, method, expected behavior, and observed behavior.
2. Confirm the exact running release SHA and verify that the route appears in the live endpoint catalog.
3. Review Module 013 platform and API evidence.
4. Review Module 016 operational evidence using the same correlation ID and time window.
5. Review Module 998 diagnostic checks and active issues.
6. Review Module 078 service, SLO, signal, and alert evidence.
7. Review Module 077 release, deployment, validation, gate, and rollback evidence.
8. Run an eligible safe API retest when authorized and explicitly confirmed.
9. Distinguish authorization, route, schema, dependency, timeout, integration, worker, deployment, and runtime causes.
10. Create a Module 076 defect when the issue remains reproducible or unresolved.

### Status interpretation

| Evidence | Interpretation |
|---|---|
| HTTP 200–299 | The request returned successfully at that time; it does not prove historical reliability or all dependency health |
| HTTP 401/403 | The current effective identity is not authorized; this is not treated as a platform outage |
| HTTP 404 | Possible route/revision mismatch, missing parameter, compatibility change, or requested record not found |
| HTTP 409/422 | Current state, validation, confirmation, or workflow precondition was not satisfied |
| HTTP 429 | Rate or concurrency control is active |
| HTTP 5xx | Application startup, dependency, schema, database, integration, worker, or internal runtime failure requires correlation evidence |
| Timeout | The endpoint, database, integration, queue, DNS/network path, or downstream dependency exceeded the diagnostic boundary |
| Registered but untested | The route exists, but no current status result was established |

## Comprehensive answer contract

Every system-intelligence answer includes relevant portions of this structure:

1. Direct conclusion
2. Executive summary
3. Scope and filters
4. Current state
5. Detailed analysis
6. API findings
7. Troubleshooting findings
8. Root-cause hypotheses
9. Safe diagnostic sequence
10. Source evidence
11. Known, unknown, stale, unavailable, and unauthorized values
12. Assumptions
13. Conflicts
14. Limitations
15. Risks and implications
16. Recommended actions
17. Future enhancement blueprint when requested
18. Navigation targets
19. Data-as-of timestamp
20. Confidence and confidence explanation

Pulse AI must not silently turn missing information into zero or present an inference as a current fact.

## Future enhancement advisor

When a user asks about a future enhancement, Pulse AI produces a detailed blueprint containing:

- requested capability and business outcome;
- affected modules and personas;
- current capabilities found in live APIs, architecture, and module evidence;
- gaps and unresolved decisions;
- proposed architecture and trust boundaries;
- proposed versioned APIs;
- data ownership and migration considerations;
- role, permission, project, customer, record, and field controls;
- private document and external-model privacy controls;
- operational support, diagnostics, SLOs, alerts, and runbooks;
- implementation phases;
- unit, migration, integration, authorization, privacy, AI-evaluation, load, failure, frontend, API, and container testing;
- Test and Production rollout;
- code, migration, prompt/model, configuration, and route rollback;
- dependencies;
- risks; and
- measurable acceptance criteria.

Pulse AI preserves owning-module authority. It does not recommend copying financial, schedule, permission, or workflow logic into Module 011.

## Durable conversations

Migration 054 creates:

- `pulse_ai_conversations`
- `pulse_ai_conversation_messages`
- `pulse_ai_system_inquiry_runs`
- `pulse_ai_system_tool_events`

Completed questions and responses are stored per user so they remain visible after:

- closing the chat;
- navigating to another module;
- reopening the chat; or
- refreshing the page.

The conversation stores the rendered answer and structured response, tool codes, source state, correlation ID, model evidence, and data-as-of timestamp.

Raw private document chunks, embedding vectors, credentials, unrestricted tool bodies, and provider secrets are not returned to the browser. Tool-response persistence is disabled by default; immutable evidence stores response checksums and sanitized summaries.

Administrator View-As does not transfer conversation or retest mutation authority. The actual administrator can read only the administrator’s own conversation history while View-As is active.

## Chat usability

The global Pulse AI chat and Module 011 workbench use:

```text
Enter        Send the question
Shift+Enter  Add a new line
Escape       Close the global chat
```

The conversation region has a definite desktop and mobile height and an independent vertical scrollbar. New messages follow the latest response only while the user remains near the bottom. Scrolling upward is not overridden.

The chat shows:

- a durable conversation selector;
- a New conversation action;
- quick questions for API inventory, troubleshooting, purpose, system status, and enhancements;
- comprehensive collapsible answer sections;
- source, API, tool, confidence, timestamp, and correlation evidence;
- API tables with local filtering;
- future enhancement blueprints; and
- direct navigation to the applicable Pulse modules.

## Permissions

| Permission | Purpose |
|---|---|
| `ASK_PULSE_AI_SYSTEM_INTELLIGENCE` | Ask detailed authorized questions about Pulse |
| `VIEW_PULSE_AI_API_INVENTORY` | View registered runtime APIs and ownership |
| `USE_PULSE_AI_SYSTEM_TROUBLESHOOTING` | Use governed troubleshooting sources |
| `USE_PULSE_AI_ENHANCEMENT_ADVISOR` | Generate future enhancement blueprints |
| `VIEW_PULSE_AI_CONVERSATION_HISTORY` | View the current user’s durable conversations |
| `RETEST_PULSE_AI_SAFE_API` | Perform an explicitly confirmed safe GET retest |
| `VIEW_PULSE_AI_SYSTEM_AUDIT` | Review system inquiry and tool evidence |

Super Administrator receives the complete surface. Other roles receive only explicitly granted capabilities. The owning endpoint still applies its own authorization before returning evidence.

## Privacy and model boundary

- Current system facts come from registered APIs and deterministic tools.
- Restricted project documents remain in the private Pulse retrieval and model boundary.
- The optional private Pulse model can synthesize the deterministic evidence into a more comprehensive explanation.
- If the private model is unavailable, Pulse returns the deterministic evidence-based answer rather than failing or sending restricted context elsewhere.
- This phase does not send private system, customer, document, financial, employee, or credential context to Claude or OpenAI.
- Module 064 remains the only approved provider, credential, health, routing, refusal, and circuit-breaker boundary for any future sanitized external reasoning.

## New API surface

```text
GET  /api/pulse-ai/v1/system/readiness
GET  /api/pulse-ai/v1/system/tools
GET  /api/pulse-ai/v1/system/apis
GET  /api/pulse-ai/v1/system/apis/{apiId}
POST /api/pulse-ai/v1/system/apis/{apiId}/retest
POST /api/pulse-ai/v1/system/questions
GET  /api/pulse-ai/v1/system/conversations
POST /api/pulse-ai/v1/system/conversations
GET  /api/pulse-ai/v1/system/conversations/{conversationId}
POST /api/pulse-ai/v1/system/conversations/{conversationId}/messages
```

## Deployment and activation boundary

This source package does not:

- apply migration 054;
- deploy Test or Production;
- change Azure, Entra, DNS, networking, storage, Container Apps, or Key Vault;
- configure a private model or external model;
- change Module 064 credentials, models, provider state, or feature routes;
- run a deployment, rollback, database, provider, project, Timesheet, financial, or permission mutation;
- execute arbitrary SQL or an arbitrary URL; or
- automatically convert conversations into training data.

Migration application, environment deployment, private-model configuration, production promotion, and any future mutating tool require separate guarded operations and validation.
