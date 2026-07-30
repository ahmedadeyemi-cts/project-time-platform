# ProjectPulse Platform Security Posture Baseline

**Baseline date:** 2026-07-30  
**Reviewed source:** `052aa1fcfa1909a0437969fe7ddcf753c7c7b2d9`  
**Scope:** repository governance, CI/CD, identity and session handling, application authorization, dependencies, containers, migrations, mirroring, rollback, and operational evidence.

## Executive assessment

The July 29 application findings have been remediated and are protected by a permanent 32-finding regression gate. The platform also has strong fail-closed authorization middleware, object-scope controls, immutable deployment-image practices, checksum-pinned migrations, non-root containers, and auditable Test deployment evidence.

The posture is not yet complete. The highest remaining risk is governance and supply-chain exposure around the repository and build system, rather than a confirmed reopened application vulnerability.

## Immediate controls in this baseline

This baseline:

1. removes completed workflows that retained automated branch-write authority;
2. adds CODEOWNERS, a pull-request security checklist, a security-reporting policy, and Dependabot coverage;
3. adds an always-on repository posture workflow for pull requests and `main`;
4. makes the primary CI workflow run on `main` pushes;
5. adds NuGet and npm vulnerability gates;
6. pins Actions used by critical CI, mirror, and rollback workflows to full commit SHAs;
7. prevents CI/build validators from silently changing tracked source;
8. restricts the corporate mirror to `main` and release tags without force/prune;
9. limits rollback to immutable, existing API and web digests in the approved ACR;
10. adds browser security headers at the web edge;
11. prevents growth in the existing backend source-transform and frontend injector debt.

## Confirmed strengths

- A central security middleware is registered after session validation and before endpoint execution.
- View-As is read-only and audited.
- Super Administrator role mutation is separately protected.
- Work Register, Project Intake, document, reporting, approval, and time-compliance routes have role and object-scope boundaries.
- Session tokens use cryptographic randomness and are stored as hashes server-side.
- Local passwords use salted PBKDF2-SHA256 and fixed-time comparison.
- API and web containers run as non-root users.
- Test deployments use exact source commits and immutable image digests.
- Migrations 051A, 052, and 053 were checksum-pinned, applied, and independently verified without changing operational row counts.
- The private Pulse AI worker and private RAG remained disabled during the cumulative Test deployment.

## P0 administrator actions

These settings cannot be guaranteed by repository source alone and must be completed in GitHub and Azure administration.

### Repository ownership and visibility

- Make the canonical repository private and organization-owned unless there is a documented business decision to publish all source, workflows, architecture, and operational material.
- Treat every long-lived secret that was usable while the repository was public as requiring rotation, even when no plaintext secret is found in the current tree.
- Rotate `PROJECTPULSE_MIRROR_TOKEN` and replace it with the smallest possible destination-repository permission.
- Decide whether the US Signal organization repository should become authoritative and eliminate bidirectional or all-ref mirroring.

### GitHub security features

Enable:

- secret scanning;
- push protection, including generic/non-provider patterns where licensed;
- Dependabot alerts and security updates;
- CodeQL default setup for C# and JavaScript/TypeScript;
- code-scanning merge protection.

### Main-branch ruleset

Protect `main` with:

- pull request required;
- at least one independent approval;
- CODEOWNER approval for protected files;
- dismissal of stale approvals;
- approval from someone other than the last pusher;
- successful `ProjectPulse CI / validate`;
- successful `ProjectPulse Repository Security Posture / repository-security-posture`;
- successful CodeQL checks;
- conversation resolution;
- blocked force pushes and branch deletion;
- no administrator bypass except an audited emergency group.

### GitHub Actions policy

- Set default workflow permissions to read-only.
- Require approval for workflows from forks.
- Restrict Actions to GitHub-owned, organization-owned, or specifically allowlisted actions.
- Enable the repository/organization requirement that actions use full-length commit SHAs after all remaining workflows have been converted.
- Review every workflow with `id-token: write` and scope its Azure federated credential to the exact repository, branch or environment, and workflow purpose.

### Environment protection

For `test` and `production`:

- require environment reviewers;
- prevent self-review for Production;
- separate Test and Production Azure identities and variables;
- restrict Production deployment branches/tags;
- retain immutable deployment and rollback evidence;
- configure a deployment wait timer or change window where appropriate.

## P1 engineering program

### Replace browser-readable session storage

The current frontend reads session tokens from `localStorage` and `sessionStorage` and sends them through several headers. A successful cross-site scripting flaw could therefore expose a live token.

Move to:

- `HttpOnly`, `Secure`, `SameSite` cookies;
- anti-CSRF tokens on unsafe methods;
- one authoritative session transport;
- session rotation after authentication and privilege changes;
- revocation of all user sessions after password or role changes;
- shorter idle timeout for privileged roles.

Until that migration is complete, the CSP and URL/output controls remain critical compensating controls.

### Eliminate build-time source rewriting

The backend project currently excludes canonical source files and compiles transformed copies created with shell commands. The frontend prebuild also runs multiple tracked-source injectors.

This creates review/build divergence and contributed directly to stacked-merge and compile failures. The target state is:

- canonical source compiled directly;
- generated code produced by a typed generator or source generator;
- generated artifacts reproducible and checked for idempotence;
- builds unable to edit tracked source;
- no new `Compile Remove` or build-time source-transform commands;
- phased reduction of the current baseline to zero.

### Expand behavioral security tests

The 32-finding gate is valuable but predominantly verifies source contracts. Add integration tests that start the API against an isolated PostgreSQL database and test:

- anonymous, inactive, expired, and revoked sessions;
- every role against every protected route and unsafe method;
- cross-user and cross-project object identifiers;
- View-As writes;
- Super Administrator creation, removal, and target protection;
- document visibility and download boundaries;
- SSO state replay and callback binding;
- upload path, URL, CSV, email-header, and archive adversarial inputs;
- authorization dependency failure behavior.

Generate the role/route cases from one authoritative permission matrix to prevent test drift.

### Software supply-chain controls

Add:

- SBOM generation for API and web images;
- container and filesystem vulnerability scanning;
- build provenance or artifact attestations;
- digest-pinned runtime base images;
- license review;
- dependency review for pull requests;
- a policy for maximum remediation age by severity.

## Operational verification

After each security-affecting Test deployment:

1. confirm the exact source commit and immutable image digests;
2. verify required migrations and checksums;
3. run anonymous and authenticated negative authorization tests;
4. test Super Administrator, Administrator, PTC, Manager, PM, Engineer, and View-As;
5. verify security headers and SSO origin/state behavior;
6. rerun the external security scanner;
7. reconcile scanner results to a tracked finding register;
8. retain evidence and remove temporary deployment controls.

## Security health indicators

Track these continuously:

- open P0/P1 findings;
- oldest unresolved high-severity dependency or code-scanning alert;
- percentage of workflows pinned to full SHAs;
- number of workflows with write or OIDC permission;
- backend build-time source transforms;
- frontend source injectors;
- temporary workflow count;
- direct pushes or bypasses to `main`;
- failed authorization tests;
- unreviewed environment deployments;
- secret-scanning push-protection bypasses.

## Acceptance criteria

The posture is considered production-ready only when:

- P0 administrator actions are complete;
- current `main` is private or publication is formally risk-accepted;
- no live credential from the public period remains valid;
- branch and environment protection are enforced;
- CodeQL, secret scanning, push protection, dependency alerts, CI, and the repository posture gate are active;
- authenticated role-based UAT and a fresh external scan pass;
- the session-cookie migration and build-source convergence have committed plans, owners, and deadlines.
