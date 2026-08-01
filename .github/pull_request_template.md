## Purpose

Describe the user or operational outcome and the exact source baseline.

## Change boundaries

- [ ] No direct commit to `main`
- [ ] Isolated branch/worktree used
- [ ] No unrelated module or deployment-control files changed
- [ ] Migration number and owner confirmed, or no migration is required
- [ ] Test/Production effects are explicitly stated

## Security review

- [ ] Authentication and authorization impact reviewed
- [ ] Object-level scope reviewed for every affected read and write
- [ ] Super Administrator, Administrator, PTC, Manager, PM, Engineer, and View-As behavior considered where applicable
- [ ] Input, file path, URL, email, CSV/export, and deserialization safety considered
- [ ] Secrets, tokens, credentials, and personal/customer data are not committed or logged
- [ ] New outbound network destinations are allowlisted and documented
- [ ] New workflows use least-privilege permissions and do not retain temporary branch-write authority
- [ ] New Actions references are pinned to a full commit SHA or have a documented exception

## Validation evidence

List the exact commands, workflow runs, migration apply/rollback checks, API tests, frontend build, container build, and role-based UAT performed.

## Deployment and rollback

State the exact release commit, immutable image expectations, migration prerequisites, rollback boundary, and whether authenticated UAT remains required.
