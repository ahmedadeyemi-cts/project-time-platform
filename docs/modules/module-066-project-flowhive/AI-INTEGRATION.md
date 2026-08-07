# Module 066D — Private-First Celar AI Integration

## Required dependency

Module 066 is a consumer of Module 011 and Module 064. It is not an AI provider
configuration module and contains no direct model client or provider secret.

Celar AI owns private document grounding and the detailed planning-reasoning
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
6. return a structured WBS draft in which every supported SOW task line carries
   its private citation IDs, estimated duration, estimated effort, and dependency
   evidence;
7. delegate each task's start date, finish date, critical path, float, and working-day
   calculations to FlowHive's deterministic schedule engine; and
8. auto-fill the editable FlowHive plan while requiring an explicit human save
   before an immutable draft version is created.

The save path recalculates and embeds each task's estimated start and finish dates
in the immutable plan version, so edits made during review cannot persist stale
preview dates.

The approved SOW Scope of Services is the primary source for committed work.
Deliverables, acceptance criteria, and explicitly in-scope statements are
supporting authority. Exclusions, options, conflicts, and unanswered questions
must remain visibly labeled and cannot be silently converted into commitments.

The returned draft uses the exact phase order `Plan`, `Design`, `Implement`,
`Validate`, and `Release`. Every executable task is assigned to one phase and
must contain enough detail for PM and engineering review, including the work
steps, required inputs, expected outputs, completion criteria, validation,
responsibility split, prerequisites, risks, and unresolved questions. The PM's
selected start and target end dates are constraints for deterministic
scheduling; the model does not calculate or invent calendar dates.

The private request uses feature `project_flowhive_plan`, a maximum output of
2,600 tokens by default and temperature 0.1. A citation-ready private plan is
required. If private SOW evidence or inference is unavailable, the request fails
closed and no generic plan or template is represented as an AI result.

## Optional external reasoning

Claude or OpenAI may be used only after separate policy approval and only
through Module 064. The payload must be a sanitized abstract reasoning capsule.
It cannot contain:

- document excerpts or document bytes;
- project or customer identity;
- record, host, network, or infrastructure identifiers;
- pricing, rate, revenue, cost, margin, or contract terms; or
- sensitive authentication material.

For the FlowHive planning capability, the external payload is a fixed,
server-owned, identity-free blueprint. Sanitization means omission, not masking:
the external provider receives no SOW text or excerpts, organization/customer/
project/person data, document names, source citations, dates, locations, URLs,
record identifiers, commercial terms, technical environment details, or copied
source substrings. The private model uses the approved SOW to specialize the
generic blueprint inside the ProjectPulse boundary.

The external result is generic reasoning assistance only. Celar AI must verify
it privately against the authoritative SOW, GSD, project records, and
deterministic schedule calculations before any content is shown as a draft.

A Claude or OpenAI safety refusal terminates routing. Neither an external answer
nor a local template can replace the required citation-grounded private plan.

## Human review sequence

The Project Manager reviews the source coverage, assumptions, conflicts, risks,
and proposed timeline before presenting the draft to Engineering. Engineering
modifies and validates the technical tasks, durations, dependencies,
prerequisites, milestones, roles, and unresolved questions before a separately
authorized user can approve a baseline.

## Source authority and human control

The response must identify SOW/GSD and supporting-document versions, cite source
sections, surface conflicts, label assumptions, preserve unknown commitments,
and explain source coverage and freshness.

AI output cannot modify canonical tasks, store a plan, establish a baseline,
assign a person, reserve capacity, create a customer artifact, publish a
customer commitment, or change an approved date without separate human-reviewed
actions.
