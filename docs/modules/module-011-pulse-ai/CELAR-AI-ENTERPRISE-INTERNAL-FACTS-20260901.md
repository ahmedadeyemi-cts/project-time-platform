# Celar AI Enterprise Internal Facts — 2026-09-01

## Purpose

This change repairs the governed internal-question path used by Celar AI Help & Search. Public/external questions already had a viable provider path, but some private enterprise questions could miss the deterministic internal-data resolver and fall into a supporting-service route. When that supporting service was unavailable, the user saw a generic `Service temporarily unavailable` response instead of a governed answer from Pulse data.

The motivating example is:

> How many active projects and how many tasks does Kevin Damisch have?

That compound question did not match the previous project-only or task-only deterministic parser, even though the underlying PostgreSQL integration and Kevin Damisch verified identity alias already existed.

## Root-cause boundary

The repair does **not** make external providers responsible for private enterprise facts. It extends the existing `CelarAiInternalDataService` so supported private questions are recognized before the generic people/supporting-service route and are answered from permission-scoped authoritative database records.

The resolver continues to fail closed:

- private question text or result rows are not sent to Claude or OpenAI;
- no arbitrary natural-language SQL is executed;
- a missing or ambiguous person/project is not guessed;
- source failure is not converted to a zero, fabricated stakeholder, or invented project history;
- the effective user's authorization scope is applied before rows are returned.

## New governed question families

### Combined person workload

Examples:

- `How many active projects and how many tasks does Kevin Damisch have?`
- `How many tasks and how many active projects does Kevin Damisch have?`
- `What is Kevin Damisch working on?`

The combined response returns the distinct active project count and active task-assignment count together. Project relationships can come from current Project Manager, Account Executive/Sales owner, Solution Architect, or governed current resource assignment records. Task counts continue to use current task-assignment authority with Work Register precedence and Module 001A closeout/effective-date filters.

### Project stakeholders

Examples:

- `Who is the Account Executive for project <project code>?`
- `Who is the sales person for <project code>?`
- `Who is the Solution Architect assigned to project <project code>?`
- `Who is the Project Manager for <project code>?`

Authority:

- Project Manager: `projects.project_manager_user_id`
- Account Executive / Sales owner: `projects.account_executive_user_id`
- Solution Architect: `projects.solution_architect_user_id`
- Identity display: active `app_users`

`Sales person`, `sales rep`, `AE`, and `Account Executive` are intentionally normalized to the existing Account Executive / Sales owner authority. No second synthetic sales identity is invented.

### Project historical context

Examples:

- `Show me the historical context for project <project code>`
- `What happened with project <project code>?`

The answer combines the authorized project record (status, description, schedule, created/updated timestamps) with immutable `work_lifecycle_audit_events` when that audit source exists and is readable. If immutable audit evidence is unavailable, Celar AI returns a partial evidence-limited answer rather than generating a generic project story.

Closed/completed projects remain eligible for project stakeholder/history lookup when the requester is authorized to the project. Closed/completed projects remain excluded from active workload counts.

## Authorization model

The implementation keeps the existing effective-user model:

- broad project scope is limited to the established privileged roles;
- project-management roles can see their managed-project scope;
- manager/lead roles can see governed team scope;
- users can see projects on which they have an authorized recorded relationship;
- project-level stakeholder/history lookup is restricted to authorized project scope;
- View-As continues to use effective-user read scope and does not turn this resolver into an unrestricted database browser.

## Database and migration impact

No new production migration is introduced by this repair. The implementation uses schema already governed in the repository, including the existing Module 080 Celar AI identity authority, existing project stakeholder fields, current assignment sources, and the existing work-lifecycle audit table.

The Module 080 integration test fixture is expanded only so its isolated test database represents the fields and audit relation already present in the real platform schema.

## Validation added

`tests/CelarAiInternalDataTests/Program.cs` now covers:

- the exact combined Kevin Damisch question from the reported failure;
- combined project/task wording in both orders;
- broader `working on` person wording;
- Account Executive / Sales owner resolution;
- Solution Architect resolution;
- Project Manager resolution;
- project historical-context parsing;
- project history database evidence;
- continued private/internal routing for these question families.

`tests/test-celar-ai-internal-data-migration-080.sh` now builds an isolated source fixture that includes project stakeholder identities and one immutable lifecycle audit event, while preserving the existing migration idempotence, alias-integrity, privilege, and guarded-rollback assertions.

## Release boundary

This PR is source/test/documentation only. It does not deploy to Protected Test or Production and it does not change Module 025. A governed release should still require normal PR CI and the established Protected-Test acceptance path before Production is considered.
