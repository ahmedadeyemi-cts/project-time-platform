# FlowHive and Project Forge Protected-UAT Release Record

**Repository:** `ahmedadeyemi-cts/project-time-platform`
**Release branch:** `fix/shared-project-document-planning-20260819`
**Protected-UAT target:** `https://phd-west-test.onenecklab.com`
**Production mutation:** `NONE`

## Objective

This release unifies FlowHive and Project Forge around one project-scoped, private, document-grounded AI planning pipeline.

The completed design resolves the selected project's existing active Work Register Statement of Work and relevant supporting project documents, queues private document processing when required, and builds a detailed project plan from the resulting citation-backed scope evidence.

No duplicate SOW upload or pasted SOW excerpt is required.

## Shared planning flow

```text
Selected authorized project
    ↓
Resolve active Work Register SOW by project ID
    ↓
Resolve current GSD, design, architecture, requirements,
proposal, order, runbook, implementation, deployment,
and other authorized project-planning documents
    ↓
Queue private scan, extraction, sanitization, and indexing
    ↓
Confirm current authoritative document versions
    ↓
Require Scope of Services citations
    ↓
Extract distinct SOW work packages
    ↓
Expand every work package into
Plan → Design → Implement → Validate → Release
    ↓
Estimate hours and working-day durations
    ↓
Create logical dependencies and milestones
    ↓
Calculate dates and critical path from project Start Date
    ↓
Persist a reviewable draft
```

## FlowHive behavior

FlowHive saves generated content only as the project's mutable Planner working copy.

The automated operation does not:

- create an immutable version;
- establish a reviewed baseline;
- approve a customer deliverable;
- assign project resources; or
- silently shorten estimated effort or duration.

PM and Engineering review remain explicit actions.

When the calculated schedule exceeds the requested project finish date, FlowHive reports the calculated finish, critical path, and options instead of compressing estimates.

## Project Forge behavior

Project Forge uses the same project-document authority and the same five-phase planning builder as FlowHive.

Forge saves generated output as an AI review draft. It does not automatically alter canonical tasks, create assignments, or adopt the draft into the live project.

Existing Forge review and adoption governance remains required.

## Evidence and privacy controls

The release requires:

- exact project-ID scoping;
- current Work Register document identity;
- current private document versions;
- authoritative or approved SOW state;
- private indexing;
- current-SOW citations;
- Scope of Services citations;
- no fabricated products, versions, quantities, licensing, interfaces, or access requirements;
- missing information converted to explicit open questions; and
- no raw private SOW/GSD text sent to an external provider.

## Task detail contract

Generated tasks support source-backed:

- descriptions and ordered implementation steps;
- products, platforms, manufacturers, and models;
- software and firmware versions;
- licensing requirements and quantities;
- tools and systems;
- interfaces and integration points;
- access requirements;
- inputs and outputs;
- customer and US Signal responsibilities;
- prerequisites and dependencies;
- acceptance criteria and validation steps;
- rollback steps;
- risks and assumptions;
- open questions;
- estimated hours and duration;
- priority and required roles; and
- source citations.

## Protected-UAT acceptance gate

The release is not complete until the exact merged source SHA is deployed to Protected UAT and authenticated validation proves:

1. Existing project SOW resolution requires no duplicate upload.
2. Private processing starts and reaches a current private version.
3. Authority, indexing, and Scope of Services citations are ready.
4. FlowHive AI Planner completes without HTTP 400, 422, or 503.
5. Every scope package contains Plan, Design, Implement, Validate, and Release tasks.
6. Durations, dates, dependencies, milestones, and critical path are populated.
7. FlowHive saves only a mutable working copy.
8. Project Forge creates only a review draft until explicit adoption.
9. Repeated Celar AI requests produce no HTTP 502, 503, or 504.
10. Production mutation remains `NONE`.

## Current evidence boundary

At the time this record was added, source validation had completed locally against the guarded release tree, but PR #737 remained open and unmerged. Protected-UAT completion must be established by GitHub Actions and runtime evidence for the exact merged SHA; local evidence alone is not a deployment claim.
