# Unified Celar AI Context Routing

## Canonical target order

Every AI-enabled capability uses the same governed target order:

1. Celar AI
2. Claude
3. OpenAI
4. Governed local template

A target advances only when the prior target is unavailable, ineligible, or below the capability quality gate. A safety refusal remains terminal and is never bypassed by a later target.

## Capability-specific context adapters

The router order is shared, but each capability compiles a different governed context envelope.

- **Ask Celar AI — public question:** sends only the isolated public question to Claude/OpenAI after the public classifier and privacy gate pass.
- **Ask Celar AI — Pulse/internal question:** uses authorized product knowledge, live tools, internal-data resolvers, and private RAG. Private context is not sent to public providers.
- **Timesheet:** uses authorized project/SOW context privately when available. Claude/OpenAI may receive only approved identity-free fact labels and a separately sanitized Engineer note. With no SOW, the same de-identified fact route can still create a detailed reviewable description.
- **FlowHive and Project Forge:** build a private citation-grounded scope scaffold first. If private inference is unavailable, Celar AI preserves that scaffold as a partial review artifact, then Claude/OpenAI may provide only the fixed identity-free five-phase planning blueprint. The private system retains cited work packages and deterministic scheduling; no raw SOW/GSD text, customer identity, people data, dates, environment detail, identifiers, or commercial values leave the private boundary.
- **Governed local:** remains the final fallback and never invents private facts or completion evidence.

## Quality and promotion gates

Public answers must be classified as public, contain no protected context, pass output privacy validation, and return a usable direct answer. Internal answers require the owning source, permission scope, evidence, citations, confidence, and freshness appropriate to the capability. FlowHive and Project Forge remain review-only until Project Manager and Engineering validation; deterministic scheduling, adoption, assignment, publication, submission, or customer commitment always remains outside model control.

## Regression repaired on August 8, 2026

The public classifier previously recognized `What`, `Why`, and `How` question forms but not clearly public `Who` officeholder questions. It also treated the ordinary acronym `US` as a possible internal identifier. As a result, `Who is the US President?` was incorrectly routed to internal product knowledge and ended as an evidence-limited local answer. Clearly public country-officeholder questions are now recognized before the conservative named-subject/acronym privacy guard, while named employees, project roles, customer records, and other internal subjects continue to fail closed inside Celar AI.

## Required release validation

The exact repair head must pass the compiled Ask classifier tests, Timesheet AI grounding, external de-identification, unified chat and attachment privacy, FlowHive, Project Forge, Celar AI enterprise routing, API Release build, repository security, and aggregate ProjectPulse CI before merge or Test deployment. Live Test acceptance must then prove the public-question route, a SOW-grounded Timesheet suggestion, a no-SOW Timesheet suggestion, and citation-preserving FlowHive and Project Forge drafts without changing provider order or exposing private evidence.
