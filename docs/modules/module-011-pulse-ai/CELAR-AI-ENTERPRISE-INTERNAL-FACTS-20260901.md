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

## Explicit current-question context

Celar AI Help & Search already lets a user select a project or a person/team before asking a question. The compiled internal-data resolver now understands that explicit current-question context instead of requiring the selected name to be repeated inside the natural-language sentence.

Examples that are now deterministic internal queries:

- Select project `P-D`, then ask `Who is the Account Executive?`
- Select project `P-D`, then ask `Who is the Solution Architect for this project?`
- Select project `P-D`, then ask `Show me the project history.`
- Select Kevin Damisch, then ask `How many projects and tasks does this person have?`
- Select Kevin Damisch, then ask `What is this person working on?`

The Help & Search UI appends an `Explicit current-question context:` envelope to the current request. The internal resolver strips that envelope before ordinary parser matching and uses only the selected project/person value to complete otherwise incomplete internal questions. Context from a different conversation is not imported.

If a selected person/team label does not resolve to one authorized person, the resolver still fails closed instead of guessing. If a selected project does not resolve to one authorized project, no stakeholder or history fact is returned.

## Query-specific source readiness

The original internal-data readiness check treated every supported source as mandatory for every internal question. That made direct project facts unnecessarily dependent on workload sources such as Engineering Resource Request assignments and Work Register roster history.

The runtime now has two readiness boundaries:

1. **Person/workload readiness** keeps the broader assignment authority because project/task workload calculations genuinely depend on those sources.
2. **Project fact readiness** uses only the minimum project-fact authority required for project resolution and authorization: `app_users`, `projects`, and current `project_assignments`.

Project stakeholder and project-history resolution therefore no longer references the Engineering Resource Request or Work Register task-assignment tables merely to answer a direct project fact. Project history continues to treat `work_lifecycle_audit_events` as optional evidence: if the audit source is unavailable, current project metadata is returned as a clearly partial history instead of a fabricated narrative.

This is source isolation, not fail-open behavior. If a source required to establish the requester's project authorization is unavailable, the project fact still fails closed.

## Authorization model

The implementation keeps the existing effective-user model:

- broad project scope is limited to the established privileged roles;
- project-management roles can see their managed-project scope;
- manager/lead roles can see governed team scope;
- users can see projects on which they have an authorized recorded relationship;
- project-level stakeholder/history lookup is restricted to authorized project scope;
- View-As continues to use effective-user read scope and does not turn this resolver into an unrestricted database browser.

The isolated project-fact scope uses current project role ownership, current project assignments, and same-team/department relationships for authorized manager/lead scope. It deliberately does not require unrelated resource-request or Work Register tables.

## Build/runtime integration

The canonical `CelarAiInternalDataService.cs` remains the reviewable enterprise-fact implementation. `Directory.Build.props` compiles a guarded generated copy under `obj/celar-ai-internal-data-resilience/` using `build/generate-celar-ai-internal-data-context-resilience.py`.

The generator is anchor-checked. A canonical source-shape change causes generation to fail rather than silently producing a different runtime contract. The generated copy adds only the context parser, the isolated project-fact scope/readiness SQL, and query-kind readiness dispatch. Generated source remains untracked.

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
- context-selected project stakeholder/history queries;
- context-selected person workload queries;
- project history database evidence;
- continued private/internal routing for these question families;
- deliberate removal of an unrelated Engineering Resource Request assignment source while direct Account Executive project resolution remains healthy.

`tests/test-celar-ai-internal-data-migration-080.sh` continues to build the isolated source fixture with project stakeholder identities and immutable lifecycle audit evidence while preserving the existing migration idempotence, alias-integrity, privilege, and guarded-rollback assertions.

## Protected-Test acceptance before Production

After merge, the governed Protected-Test release should prove the behavior against the real Pulse database, including:

- Kevin Damisch combined active-project and active-task answer;
- context-selected Kevin Damisch workload answer;
- Account Executive/Sales owner lookup;
- Solution Architect lookup;
- Project Manager lookup;
- project history with current immutable audit evidence;
- a closed/completed project history lookup;
- an unauthorized person/project request that fails closed;
- direct project stakeholder lookup while an unrelated supporting workload source is deliberately unavailable or simulated as unavailable.

Production should not be considered validated from CI alone.

## Release boundary

This PR is source/test/documentation only. It does not deploy to Protected Test or Production and it does not change Module 025. A governed release still requires normal PR CI and the established Protected-Test acceptance path before Production is considered.
