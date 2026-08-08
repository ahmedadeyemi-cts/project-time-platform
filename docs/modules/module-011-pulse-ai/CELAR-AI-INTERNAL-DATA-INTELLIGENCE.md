# Celar AI Internal-Data Intelligence

## Outcome

Celar AI applies an internal-first boundary to Help and Production Chat.
Questions about Pulse people, projects, tasks, assignments, customers, metrics,
documents, workflows, or platform state stay inside authenticated Pulse data
services. Claude and OpenAI are eligible only for a clearly public
general-knowledge question.

An ambiguous question stays internal. A caller can make public intent explicit
with wording such as `outside Pulse` or `public general knowledge`.

| Question class | Execution boundary | External fallback |
|---|---|---|
| Supported deterministic internal fact | Permission-scoped PostgreSQL resolver | Prohibited |
| Other Pulse product/live-data question | Existing local tools, product knowledge, and private RAG | Prohibited |
| Missing, ambiguous, unauthorized, or unavailable internal source | Partial answer with the exact failure class | Prohibited |
| Clearly public general knowledge | Module 064 governed Claude/OpenAI route | Allowed |

## Initial deterministic resolvers

Contract `celar-ai-internal-data-v1-20260807` supports natural-language count
and list questions for a person's current projects and tasks. The reported
failure is covered directly:

> How many projects does Kevin Damisch have assigned to him?

The count is calculated from distinct visible active project IDs. A project is
included when the resolved person is the Project Manager or has a current
assignment in at least one authoritative assignment source. Multiple tasks or
mirrored rows never double-count a project.

Current task rows are deduplicated by project, task, and person. An active Work
Register roster row takes precedence over its mirrored `project_assignments`
row.

## Assignment authority

The resolver combines current evidence from:

1. `projects.project_manager_user_id` for Project Manager ownership;
2. effective, non-closed `project_assignments` rows;
3. active `work_register_task_assignment_history` roster rows; and
4. committed `engineering_resource_request_assignments` linked to a project.

Project-level resource-request evidence contributes to a project count but not
to a task count. Proposed or pending resource requests do not establish a
current assignment. Closed projects, ended assignments, closed Module 001A
assignments, inactive tasks, and cancelled/rejected/closed resource requests
are excluded.

This union is required because legacy and current Pulse modules can hold an
authoritative assignment before or without a mirrored row in
`project_assignments`.

## Identity resolution

The resolver accepts one exact active display name, exact email address, or
active verified alias inside the caller's authorized scope. It never silently
selects a spelling-distance match. Near matches are suggestions only.

Migration `080_celar_ai_internal_data_intelligence` adds governed aliases with
verification evidence. It also adds the known `Kevin Damisch` legacy-name
correction only when exactly one active `Kevin Damish` directory identity
exists. Zero or multiple legacy candidates result in no seed and require
authorized review.

Global directory existence is not disclosed. Identity lookup joins the
permission-scoped people set before matching, so an out-of-scope person and an
unknown person have the same safe result.

## Authorization and privacy

Every query resolves the actual and effective user on the server. Project and
person visibility follow Super Administrator, Project Team Coordinator,
Executive, Project Manager, self-assignment, manager/team-lead, department,
team, reporting-relationship, and explicit team-scope rules. View-As uses the
effective user's record scope and does not persist a conversation under the
impersonated identity.

Question text, identity values, source rows, and calculated results are never
sent to Claude or OpenAI. Internal source failure returns a partial answer with
a correlation ID and a zero-confidence explanation; it does not infer zero or
use conversation history as evidence.

## Answer and trust contract

A completed answer includes:

- a direct numeric conclusion;
- identity resolution method;
- permission, status, date, and closeout filters;
- distinct-count definition;
- source citations and data-as-of time;
- up to 100 detail rows while retaining the complete count; and
- an explicit statement that no external provider was called.

`questionAnswered` is true only for a completed answer with the required shape
and successful current evidence. A provider access disclaimer, ambiguous
identity, authorization boundary, or unavailable source is classified as
insufficient evidence. External-provider non-answers no longer inherit a
fixed 72 percent confidence value.

## Failure classes

| Status | Meaning | Numeric value inferred |
|---|---|---|
| `completed` | One authorized identity and authoritative query succeeded | Yes |
| `partial / ambiguous_person_identity` | More than one authorized exact/verified match | No |
| `partial / person_not_found` | No exact identity in authorized scope | No |
| `partial / source unavailable` | Database, schema, transport, or timeout failure | No |

## Deployment and rollback

Apply migration 080 before activating the source revision. The coordinated Test
release also applies migration 081, which admits eligible private documents
through a dedicated least-privilege service identity and repairs supported
Work Register file extensions without changing internal-data query authority.
Both migrations are idempotent. Migration 080 rollback removes the reproducible
migration-owned Kevin alias, but refuses to drop the alias table while any
operator-created alias remains.

CI compiles the API and parser contract, validates internal/public routing
examples, applies the migration twice, verifies alias constraints and the
known correction, exercises guarded rollback, and verifies clean rollback.

## Extension rule

New internal question families must register a deterministic or governed
read-only resolver, identify authoritative sources, enforce effective-user
scope before identity/record matching, define zero versus unknown, cite the
result, and add boundary tests. Adding a resolver never makes its question
eligible for external fallback.
