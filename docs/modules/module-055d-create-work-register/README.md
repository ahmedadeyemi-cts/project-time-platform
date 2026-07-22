# Module 055D — Create New Project

Module 055D is the only new-project creation page. A Project Team Coordinator
(`PROJECT_TEAM_COORDINATOR`), Administrator (`ADMINISTRATOR`), or Super
Administrator (`SUPER_ADMINISTRATOR`) can open and execute its create APIs.
Project Managers and Project Management Leads do not inherit 055D creation
authority. View-As is always read-only.

## Creation sources

### Import from GSD

The coordinator uploads the GSD and supporting SOW/approval documents. The
existing controlled extraction flow prepares project, task, hour, assignment,
and pricing fields for review before final creation.

### Import from SELL

The coordinator supplies a SELL record ID and the matching ProjectPulse
customer. Module 055D uses the server-side Module 026 connection—OAuth 2.0 or
write-only API key—to retrieve the record. The configured Module 026 lookup URL
must contain a literal `{recordId}` placeholder, and its mapping determines the
approved SELL fields.

SELL is authoritative for:

- project name;
- SELL quote/reference; and
- Actual Rate / Pricing / Rate Review rows.

Those fields are read-only in the review UI and are re-imposed from the
server-held extracted snapshot whenever the review is saved. Client-side
tampering therefore cannot replace the authoritative values.

## Audit and security

The intake-package creation and review history record the actual coordinator,
source, reason, record reference, and source-lock evidence. Final creation also
writes `work_register_created` evidence to `work_register_change_history` in
the same database transaction as project creation,
which is visible in Module 055C's Audit tab.

The SELL response is size-bounded. Only mapped operational fields are retained;
the raw provider response and credentials are not stored. Public-HTTPS and
server-side credential protections are inherited from Module 026.

## Release boundary

Migration `036_work_register_role_scope_and_closeout_handoff.sql` aligns the
permission catalog with the PTC/Administrator creation policy. Provider
credentials, OAuth consent, provider calls, database migration, and deployment
each require their own governed production action.
