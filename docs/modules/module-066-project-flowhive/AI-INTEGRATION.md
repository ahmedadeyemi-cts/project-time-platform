# Module 066D — Private-First Pulse AI Integration

## Required dependency

Module 066 is a consumer of Module 011 and Module 064. It is not an AI provider
configuration module and contains no direct model client or provider secret.

Pulse AI owns private document grounding and the detailed planning-reasoning
contract. Module 064 remains the governed provider, health, usage, routing,
circuit-breaker, and fallback boundary.

## Private planning path

A complete FlowHive planning request must:

1. resolve the current effective user and authorized project scope;
2. retrieve the approved SOW, GSD, architecture, order, and supporting documents
   inside the private ProjectPulse boundary;
3. identify the exact document versions and source sections used;
4. extract scope, exclusions, deliverables, responsibilities, prerequisites,
   acceptance criteria, constraints, quantities, risks, dependencies, and open
   questions;
5. send the detailed context only to an approved private ProjectPulse model;
6. return a structured WBS draft with citations, assumptions, conflicts, and
   unresolved inputs; and
7. delegate dates, critical path, float, working days, and dependency
   calculations to FlowHive's deterministic schedule engine.

The private request uses feature `project_flowhive_plan`, a maximum output of
2,600 tokens by default, temperature 0.1, and a governed local deterministic
supplied-task fallback. Private model execution remains separately gated.

## Optional external reasoning

Claude or OpenAI may be used only after separate policy approval and only
through Module 064. The payload must be a sanitized abstract reasoning capsule.
It cannot contain:

- document excerpts or document bytes;
- project or customer identity;
- record, host, network, or infrastructure identifiers;
- pricing, rate, revenue, cost, margin, or contract terms; or
- sensitive authentication material.

The external result is generic reasoning assistance only. Pulse AI must verify
it privately against the authoritative SOW, GSD, project records, and
deterministic schedule calculations before any content is shown as a draft.

A Claude or OpenAI safety refusal terminates routing with no fallback. The local
template cannot be used to bypass a refusal.

## Source authority and human control

The response must identify SOW/GSD and supporting-document versions, cite source
sections, surface conflicts, label assumptions, preserve unknown commitments,
and explain source coverage and freshness.

AI output cannot modify canonical tasks, store a plan, establish a baseline,
assign a person, reserve capacity, create a customer artifact, publish a
customer commitment, or change an approved date without separate human-reviewed
actions.
