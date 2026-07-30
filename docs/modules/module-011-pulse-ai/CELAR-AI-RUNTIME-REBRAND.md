# Module 011 — Celar AI Runtime Rebrand

**Platform:** Pulse  
**Visible intelligence brand:** Celar AI  
**Technical compatibility identity:** Pulse AI  
**Module:** 011  
**Source branch:** `feature/module-011-celar-ai-runtime-rebrand-20260730`  
**Exact source base:** `feature/module-011-system-intelligence-troubleshooting-20260730@0abcbf5a1248d490a824ee4d987c8669344f0209`  
**Classification:** US Signal Internal — Confidential  
**Phase:** Runtime presentation, functional chat, and Module 064 relationship

## Purpose

This package converts the user-facing Module 011 identity from **Pulse AI** to **Celar AI** while preserving the existing technical contracts required for safe integration, rollback, audit, and staged deployment.

Celar AI is the unified operational intelligence system for the US Signal Solution Provider division. It was conceived and engineered under the direction of **Dr. Ahmed Adeyemi, Manager of Professional Services**, to create a central intersection where consulting teams can convene, collaborate, and exchange project, delivery, operational, and financial information.

The name draws from **Celeritas**, associated with swiftness or speed, and from the conventional symbol **c** for the speed of light in `E=mc²`. It connects US Signal's fiber-network and digital-infrastructure foundation with the Professional Services mission to translate the **speed of light into the speed of delivery**.

## Changepoint catalyst

Changepoint served as a functional legacy professional-services automation platform and system of record, but it also created operational drag:

- project, customer, delivery, time, and financial information was distributed across disconnected workflows;
- consultants spent technical time navigating rigid administrative paths;
- SOW preparation and review relied on manual transfers and repeated entry;
- project handoff and task context could become fragmented;
- time entry and task maintenance created an administration tax;
- project financial health and billing readiness were not always available in one intuitive view; and
- invoice routing and closeout depended on slower manual coordination.

Celar AI addresses that friction by unifying authorized documents, live Pulse data, governed workflows, API inventory, troubleshooting evidence, reporting and financial context, and AI-assisted reasoning without removing source-system ownership, permissions, audit, or human approval.

## Canonical answer

When a user asks **“What is Celar AI?”**, **“Tell me about Celar AI,”** **“Who created Celar AI?”**, or a similar identity question, the system returns the approved creator, name-origin, fiber, speed-of-delivery, Changepoint, privacy, governance, and operating-model narrative.

The answer is stable product knowledge and does not require a customer record, project document, financial query, public model, or external provider call.

## Functional chat

The global assistant and Module 011 workbench retain the complete system-intelligence package from the dependent source baseline.

### Keyboard and visibility behavior

- **Enter** sends the question.
- **Shift+Enter** inserts a new line.
- **Escape** closes the global chat.
- The conversation has a definite desktop and mobile height.
- The message region owns vertical scrolling.
- New messages follow only while the user is near the bottom.
- Scrolling upward is not overridden.
- Completed messages remain visible after closing the panel, navigating elsewhere, or refreshing when migration 054 is available.
- Existing historical messages are displayed with the Celar AI visible brand without rewriting immutable database evidence.

### Question behavior

The new public-facing endpoint is:

```text
POST /api/celar-ai/v1/chat
```

Identity questions use the canonical Celar AI knowledge profile. Other questions delegate to the comprehensive system-intelligence engine and preserve:

- live ASP.NET endpoint discovery;
- authorized operational tools;
- API troubleshooting;
- release and deployment evidence;
- defects and observability;
- private document and RAG readiness;
- project, Timesheet, FlowHive, reporting, and financial context where authorized;
- future-enhancement blueprints;
- source evidence;
- known, unknown, stale, unavailable, and unauthorized distinctions;
- assumptions, conflicts, limitations, risks, and actions; and
- confidence and data-as-of evidence.

## Public-facing Celar AI API surface

```text
GET  /api/celar-ai/v1/about
GET  /api/celar-ai/v1/provider-bridge/readiness
POST /api/celar-ai/v1/chat
```

The existing `/api/pulse-ai/*` APIs remain active compatibility contracts for current callers, conversation history, internal services, tests, migrations, and rollback.

## Navigation

The preferred visible route is:

```text
#celar-ai
```

The following compatibility routes still resolve to Module 011:

```text
#pulse-ai
#work-task-builder
```

Project creation and project/task management remain with Modules 055D and 055C. Celar AI does not reclaim the retired Work Task Builder responsibilities.

## Module 064 relationship

Yes, Celar AI is represented on the AI Provider Configuration Center.

Celar AI is **not** treated as an external vendor provider. It is the private operational-intelligence orchestrator and a governed consumer of Module 064 provider routes.

Module 064 remains responsible for:

- provider credentials;
- approved models;
- provider health;
- feature routing;
- timeouts and rate limits;
- circuit breakers;
- usage evidence;
- safety-refusal handling; and
- sanitized external fallback.

The Module 064 page now displays:

- the Celar AI orchestration role;
- the Module 064 provider-governance role;
- private Celar AI model readiness;
- whether confidential context is eligible for the private route;
- feature-specific routing policy; and
- the prohibition on sending raw internal documents to public providers.

Secret values and endpoint values are not returned to the browser.

## Private-first feature routing

| Feature | Primary path | External policy |
|---|---|---|
| Celar AI system chat | Private Celar model or deterministic system synthesis | Sanitized generic reasoning only |
| Timesheet document grounding | Private Celar model | Raw-document public route prohibited |
| System Help and Search | Private Celar model plus governed tools | Sanitized generic reasoning only |
| FlowHive document planning | Private Celar model plus deterministic scheduler | Generic planning checklist only |
| Reporting and financial insight | Deterministic Pulse tools plus private Celar explanation | Disabled by default |

## Technical compatibility boundary

The first runtime-rebrand package intentionally preserves:

```text
PulseAi... C# class names
/api/pulse-ai/* compatibility APIs
pulse_ai_* database objects
PULSE_AI permission codes
PROJECTPULSE_PULSE_AI environment variables
existing migration IDs
existing feature-code compatibility contracts
```

This separates visible branding from high-risk technical renaming. Removing these compatibility identifiers requires a later inventory of callers, telemetry, database references, permissions, environment settings, deployment controls, audit evidence, and rollback paths.

## Security and privacy

- Pulse authentication and effective-user authorization occur before data retrieval.
- The model is not the authorization authority.
- View-As remains read-only and does not transfer mutation authority.
- Raw SOW, GSD, customer, contract, architecture, employee, rate, and financial information remains inside the approved private boundary by default.
- Claude or OpenAI may receive only a policy-approved sanitized generic reasoning capsule through Module 064.
- A safety refusal terminates routing and is never bypassed by another provider.
- Conversations do not automatically become training data.
- No arbitrary SQL, URL, or mutation tool is accepted.

## Brand governance

Celar AI is strategically aligned with US Signal's fiber and solution-provider mission, but `Celar` is not globally unique. Before public marketing, trademark filing, domain acquisition, or customer-facing launch, US Signal Legal and Marketing should complete name-clearance, trademark, pronunciation, domain, and digital-identity review.

## Activation boundary

This source package does not:

- apply a migration;
- merge or deploy itself;
- rename database objects;
- remove Pulse AI compatibility APIs;
- change provider secrets;
- configure a private model endpoint;
- change Module 064 provider state or routing;
- change Azure, Entra, DNS, storage, networking, Key Vault, or Container Apps;
- call Claude or OpenAI;
- upload or process a customer document;
- mutate a Timesheet, project, FlowHive plan, report, financial record, or permission; or
- modify protected deployment controls.
