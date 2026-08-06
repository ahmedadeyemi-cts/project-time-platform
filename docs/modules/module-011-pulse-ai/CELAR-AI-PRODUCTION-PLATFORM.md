# Celar AI Production Platform

## Purpose

Celar AI is the unified operational-intelligence platform for the US Signal Solution Provider division. It was conceived and engineered under the direction of **Dr. Ahmed Adeyemi, Manager of Professional Services**, to create a private, permission-aware intersection where teams can retrieve evidence, answer questions, draft delivery artifacts, troubleshoot Pulse, and increase the speed of delivery.

The name draws from *Celeritas*—swiftness or speed—and the conventional symbol `c` for the speed of light. It connects US Signal's fiber-network foundation with the Professional Services mission of moving scope, delivery, evidence, approval, billing readiness, and organizational learning more quickly.

## Production architecture

```text
Pulse user question or delivery request
                  |
     Authentication and effective-user scope
                  |
          Intent-first orchestration
                  |
  +---------------+----------------+----------------+
  |               |                |                |
Utility facts  Product guidance  Private RAG   Governed live tools
Date/version   Modules/how-to     SOW/GSD/IQS  Projects/time/finance/APIs
  |               |                |                |
  +---------------+----------------+----------------+
                  |
      Deterministic business engines
       Financial formulas and FlowHive scheduling
                  |
          Private Celar AI model
                  |
       Claim, confidence, and evidence gate
                  |
       +----------+------------------+
       |                             |
Sufficient evidence          Generic reasoning gap
       |                             |
Private verification           DLP sanitization
       |                             |
       |                         Module 064
       |                             |
       |                     Claude or OpenAI
       |                             |
       +----------+------------------+
                  |
        Private reassembly and review
                  |
Detailed cited answer, Timesheet description, SOW,
FlowHive plan, timeline, diagram, or troubleshooting result
```

## One authoritative Module 011 application

Module 011 contains populated workspaces for:

1. **Overview** — US Signal architecture diagram, readiness, trust, platform identity, and solution composer.
2. **Knowledge & RAG** — private document processing, authority, extraction, embeddings, retrieval, citations, and revocation.
3. **Tools & Coverage** — live API inventory, governed tools, system intelligence, and troubleshooting.
4. **Datasets** — immutable reviewed private artifact references and SHA-256 checksums.
5. **Training** — evaluation-only, supervised fine-tuning, LoRA, QLoRA, and distillation-candidate orchestration.
6. **Evaluations** — frozen correctness, privacy, permission, leakage, citation, and feature-quality gates.
7. **Model Registry** — versioned model and adapter artifacts, lineage, checksums, states, and rollback evidence.
8. **Deployments** — Development, Test, and Production deployment plans with explicit human approval.
9. **Governance** — answer trust, routing, privacy, authorization, audit, and non-negotiable operating rules.

The Overview reuses the US Signal Celar AI architecture diagram and shows **Created by Dr. Ahmed Adeyemi**.

## Answer correctness

Celar AI routes each question before selecting sources:

- `What day is it today?` uses the current API request clock and the browser's IANA time zone.
- `What is the system version?` uses current assembly, environment, release, and revision metadata.
- `What can Celar AI answer?` uses the governed capability catalog.
- `How do I enter my time?` uses the source-controlled Module 001 operating guide.
- `What is my team working on this week?` uses authorized assignments, open work, capacity, approvals, and FlowHive evidence.
- `Which APIs are running?` uses the current ASP.NET endpoint registry.
- `Why did this request return 403?` uses authorization, API, diagnostic, release, and observability evidence.

Every answer receives a visible trust classification:

- Verified current fact
- Verified document fact
- Verified with limitations
- Calculated or verified
- Procedure
- Platform capability
- Reviewable draft
- Insufficient evidence
- Unavailable
- Unauthorized

A current factual response cannot be marked verified when no authoritative source succeeds. API counts and generic diagnostic boilerplate are not substitutes for an answer.

## Project FlowHive production generation

Project FlowHive uses:

```http
POST /api/project-flowhive/ai/production-generate
```

The existing `/api/project-flowhive/ai/generate` compatibility route remains separate. The production route returns the detailed plan-and-schedule contract and therefore avoids duplicate endpoint registration.

The production route:

1. resolves actual and effective user authorization;
2. resolves the authorized project;
3. retrieves authorized SOW, GSD, IQS, design, architecture, and project evidence;
4. generates a cited private plan draft;
5. uses optional generic sanitized Module 064 assistance only when policy and the request allow it;
6. converts the generated tasks into the Module 066 planning contract;
7. validates WBS, dependencies, assignments, and constraints;
8. calculates the deterministic weekday schedule, critical path, float, and planned hours; and
9. returns tasks, schedule, citations, assumptions, risks, conflicts, missing evidence, confidence, and review controls.

It does **not** persist or baseline the plan, assign an Engineer, reserve capacity, approve work, publish a customer artifact, or commit a date.

## Fine-tuning lifecycle

The production lifecycle stores metadata and immutable references for:

- Dataset versions
- Training jobs
- Evaluation runs
- Model versions
- Model deployment plans
- Answer-quality evidence
- Lifecycle audit events

Large datasets and model artifacts remain in approved private storage. Pulse stores artifact URIs and checksums, not raw training examples or model binaries.

The private training adapter uses:

```text
PROJECTPULSE_CELAR_AI_TRAINING_ENABLED
PROJECTPULSE_CELAR_AI_TRAINING_ENDPOINT
PROJECTPULSE_CELAR_AI_TRAINING_BEARER_TOKEN
PROJECTPULSE_CELAR_AI_TRAINING_HOST_ALLOWLIST
```

The endpoint must pass private-endpoint policy. Endpoint and token values are never returned to the browser.

Training submission uses the dedicated `PulseAiPrivateTraining` transport. Like private OCR, embedding, and inference, it disables redirects, cookies, and proxies; validates the configured allowlist; resolves only private addresses; and pins the connection to a revalidated private address. Module 064 reports the training adapter as optional when disabled and requires configuration, authentication, and private DNS verification when enabled.

## Knowledge and context fabric

Module 064 combines four related structures into one metadata-only readiness view:

1. The route graph maps every AI capability and in-system consumer to its saved eligible target order.
2. The content graph maps project, document, authoritative version, section or worksheet, searchable chunk, and citation relationships.
3. The temporal and policy context graph records evidence-as-of time, current authoritative versions, effective permissions, privacy eligibility, and route revisions.
4. The operational decision trace records the capability, configured route, selected target, outcome code, safe correlation ID, and evaluation time without exposing prompts, content, endpoints, secrets, vectors, or hidden chain-of-thought.

Freshness is explicit. Pending SOW processing and unembedded chunks appear as a refresh-pending state; current indexes report their latest authoritative indexing time. This lets operators distinguish a connected route from a fully current private-knowledge path.

## Runtime lifecycle schema

An actual-session Super Administrator or Administrator initializes the idempotent lifecycle metadata schema from Module 011 Governance. View-As remains read-only.

The runtime schema identifier is:

```text
celar_ai_production_platform_runtime_v1
```

It creates:

```text
celar_ai_dataset_versions
celar_ai_training_jobs
celar_ai_evaluation_runs
celar_ai_model_versions
celar_ai_model_deployments
celar_ai_answer_quality_events
celar_ai_lifecycle_audit
```

The initialization is explicit and audited. It does not modify customer, project, Timesheet, financial, role, provider, Azure, Entra, or deployment records.

## Module 064 boundary

The default capability route is:

```text
Celar AI -> Claude -> OpenAI -> Governed local template
```

Route order can be changed by approved capability, but it cannot weaken privacy policy. The governed local template remains the final fallback. A safety refusal ends routing.

Raw SOW, GSD, IQS, customer, project, employee, financial, credential, private endpoint, IP address, hostname, and proprietary architecture content is not eligible for direct public-provider routing.

## Private inference configuration

Private RAG and inference require the existing Module 011 private runtime settings:

```text
PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED=true
PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT=<approved private inference endpoint>
PROJECTPULSE_PRIVATE_INFERENCE_MODEL=<approved model or deployment>
PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN=<secret reference when required>
PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST=<approved private hosts>
```

Document processing additionally requires the private scanner, extraction/OCR, embedding, and indexing services described in `PRIVATE-RUNTIME-ACTIVATION.md`.

## Required release validation

The exact PR head must pass:

- .NET 10 API compilation
- JavaScript syntax validation
- Source-boundary validation
- Production injector idempotence
- Celar AI production contract validation
- Full frontend production build
- Existing ProjectPulse regression validators
- Production web-container build
- Repository security checks
- Authenticated Test UAT for Super Administrator, Administrator, PTC, PM, Engineer, and View-As
- Basic competency questions at 100%
- FlowHive private generation and deterministic schedule review
- Private-document and sanitized-external-fallback boundary tests

No model or deployment is promoted solely because training completed. Required evaluations and human approval remain mandatory.
