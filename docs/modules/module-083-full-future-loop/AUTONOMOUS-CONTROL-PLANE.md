# Module 083 — Autonomous Full Future Loop Control Plane

## Purpose

This document defines the enterprise operating model that evolves Module 083 from a persistent Full Future Loop sandbox into a bounded, policy-driven automation control plane.

The control plane coordinates change intent, governance, private development evidence, build and canary verification, release promotion, read-only production evidence, support intake, private repair, re-promotion, and final verification. It is designed to make deployment and maintenance repeatable without giving any single AI model, application process, credential, or workflow unrestricted authority.

## Enterprise autonomy definition

For Pulse, **fully autonomous** means that routine work can progress without manual coordination when every required policy, identity, evidence, environment, and rollback gate is satisfied. It does not mean unrestricted self-modification.

The operating contract is:

- automation may observe, classify, correlate, prepare, execute approved runbooks, collect evidence, and recover within an explicit policy envelope;
- all actions are idempotent, attributable, time bounded, replay safe, and auditable;
- the system fails closed when identity, evidence, policy, secrets, target environment, or rollback state cannot be proven;
- Test automation may run end to end under an approved Test policy;
- Production promotion, schema change, security exception, secret rotation, and infrastructure mutation require the configured human or environment approval unless a separately approved emergency rollback policy applies;
- Celar AI may advise, summarize, classify, and propose a repair, but it cannot approve its own recommendation or expand its own authority;
- Administrator View-As remains read-only;
- every external adapter can be disabled independently through a global kill switch and an adapter-specific circuit breaker.

## Repository strategy

A new repository is not required for the first autonomous-control-plane implementation. The authoritative Pulse source, Module 083 state machine, RBAC, audit model, validators, and reusable workflow callers should remain in `ahmedadeyemi-cts/project-time-platform` so they share one version, one pull-request review, one permission model, and one release identity.

The diagram label “Public / Prod Repo” means a **curated production release boundary**. It must not imply that private enterprise source becomes publicly visible.

A separate private repository may be introduced later as a separation-of-duties control, for example `pulse-release-control`. That repository would contain only:

- reusable deployment and rollback workflows;
- signed release manifests and policy bundles;
- environment-specific deployment templates;
- attestations, SBOM references, checksums, and immutable release evidence;
- no private development history, customer documents, application secrets, or unrestricted cloud credentials.

The separate repository is recommended only when the organization wants deployment-controller ownership to be independent from application-source ownership. It is not a prerequisite for the current build.

## Control-plane components

### 1. Intent and governance service

Receives an authorized change request, derives the change class, identifies affected modules and environments, and creates a versioned decision packet. Deterministic rules select the minimum required approval path. AI classification may assist, but deterministic policy remains authoritative.

### 2. Policy decision point

Evaluates a versioned policy bundle against the work item, actor, repository, source commit, requested environment, migration scope, security classification, test results, observability state, maintenance window, and rollback readiness.

The result is exactly one of:

- `auto_execute` — all required controls are satisfied and the action is inside the approved autonomy envelope;
- `approval_required` — the action is valid but requires a named human or protected-environment approval;
- `blocked` — a required control is missing, invalid, expired, inconsistent, or outside policy.

Every decision includes reason codes and the policy version used.

### 3. Orchestrator and durable work queue

A background orchestrator advances work through durable steps. It uses database leases, idempotency keys, optimistic revision checks, bounded retries, exponential backoff, and dead-letter handling. A process restart or duplicate delivery cannot cause a duplicate deployment, issue, promotion, or rollback.

The orchestrator never stores long-lived GitHub or cloud credentials in Module 083 tables. It requests short-lived credentials through the approved adapter boundary.

### 4. Adapter registry

External systems are reached only through registered adapters. Each adapter exposes:

- adapter identity and version;
- supported capabilities;
- allowed environments and repositories;
- credential source name, never the credential value;
- readiness, last successful probe, and circuit state;
- rate-limit and retry policy;
- dry-run support;
- required approval class;
- evidence normalization contract.

Initial adapters are:

- GitHub repository, pull request, check, issue, deployment, and workflow adapter;
- build, test, and disposable-canary adapter;
- Azure Container Apps deployment and rollback adapter executed through GitHub Actions with OIDC;
- ACR image and artifact evidence adapter;
- Azure Monitor, Application Insights, and Log Analytics read-only evidence adapter;
- Module 076 defect and repair adapter;
- Module 065 notification adapter;
- Module 011/064 Celar AI advisory adapter;
- Module 008 immutable audit projection.

### 5. Release manifest service

Every promotable release has one immutable manifest containing:

- repository and exact source commit;
- pull request and approval evidence;
- API and web image digests;
- build workflow and run attempt;
- dependency and vulnerability scan results;
- SBOM and provenance references;
- migration identifiers and checksums;
- canary scenario and result;
- target environment identity;
- configuration fingerprint with secret values removed;
- prior known-good rollback digests;
- verification suite and acceptance thresholds;
- approval evidence;
- expiration and retention policy.

Tags alone are never sufficient release identity. Deployment uses immutable digests and exact commits.

### 6. Canary controller

Creates a disposable verification context, seeds the approved scenario, executes contract, authorization, security, migration, integration, and UI checks, captures evidence, and proves cleanup. Canary data is synthetic or explicitly approved. A failed cleanup blocks promotion.

### 7. Deployment and rollback controller

The application control plane requests a deployment; the protected GitHub workflow performs the cloud action. The workflow proves repository, branch, source SHA, environment, Azure subscription, resource group, Container App identity, ACR ownership, current image, candidate image, and rollback image before mutation.

A failed release-blocking verification automatically restores the exact prior API and web digests when the rollback policy allows it. A new build is never substituted for the previously verified rollback target.

### 8. Production evidence watcher

Consumes read-only health, SLO, error, release, and user-signal evidence. It groups duplicates, associates evidence with an exact release, applies suppression and severity rules, and creates a normalized private repair item. It cannot modify production telemetry or private source.

### 9. Agent Keep

Agent Keep provides evidence-scoped support and maintenance guidance. It may search approved documentation, release manifests, normalized production evidence, known issues, and repair history. It may create a governed support or defect record through an approved adapter.

It cannot read unrestricted private source, retrieve secret values, approve a release, merge a pull request, change infrastructure, or bypass policy. Any future code-repair proposal is created on an isolated branch and must pass the same review and promotion gates as human-authored work.

## Autonomy levels

### Level 0 — Observe

Read health, repository metadata, checks, releases, deployments, logs, and Module 083 state. No external writes.

### Level 1 — Coordinate

Create internal Module 083 events, notifications, evidence packages, and Module 076 defects. GitHub issue creation may be enabled with a dedicated write permission.

### Level 2 — Test autonomous

Dispatch approved CI, create disposable canaries, deploy exact digests to protected Test, execute UAT, and automatically roll Test back after a failed gate.

### Level 3 — Production assisted

Prepare a signed Production manifest and request protected-environment approval. After approval, execute the exact deployment, verify it, and automatically roll back on a qualifying failure.

### Level 4 — Policy-bounded Production autonomous

Available only after separately approved operating history. Low-risk, pre-authorized release classes may promote during an approved window. Security, migration, secret, infrastructure, and high-impact changes remain approval gated. Emergency rollback may execute automatically to the exact known-good digest when SLO and identity conditions are proven.

## Required safeguards

- least-privilege GitHub App rather than a personal access token;
- separate Test and Production GitHub Environments;
- separate Azure federated identities for Test and Production;
- no static cloud secret when OIDC is supported;
- branch protection and required status checks;
- CODEOWNERS or required reviewers for workflow, policy, migration, and security paths;
- immutable artifact digests and signed provenance;
- SBOM, dependency, secret, malware, and vulnerability scanning;
- fail-closed environment allowlists;
- global automation kill switch;
- per-adapter disable switch and circuit breaker;
- maximum retry, maximum runtime, and maximum concurrent-run controls;
- concurrency locks by environment and release;
- database lease expiry and orphan-run recovery;
- append-only action, approval, evidence, and policy-decision history;
- explicit data classification and retention;
- no raw secrets, tokens, private keys, customer documents, or unrestricted logs in evidence;
- periodic credential, permission, restore, rollback, and disaster-recovery tests.

## Required external access

### GitHub App

Create one private GitHub App dedicated to the Full Future Loop. Install it only on approved repositories. Begin with:

- Metadata: read;
- Contents: read;
- Pull requests: read;
- Checks: read;
- Actions: read;
- Deployments: read;
- Issues: read and write only when automated issue creation is enabled;
- Commit statuses: read and write only when Module 083 publishes a normalized gate;
- Workflows: write only if workflow dispatch is required and GitHub App policy permits it.

Do not initially grant branch, content, pull-request, or administration write permission. A future autonomous repair phase can add narrowly scoped branch and pull-request write permissions after separate review.

### GitHub Environments

Required:

- `test` — Test variables, OIDC identity, protected Test UAT session, runtime token references, and optional reviewers;
- `production` — separate variables and OIDC identity, mandatory reviewers, restricted deployment branches, and wait timer if required;
- optional `canary` — disposable validation resources when separated from Test.

### Azure

Use federated identities with the smallest possible scope:

- Test deploy identity scoped to the exact Test resource group, ACR operations required by the workflow, and approved Key Vault references;
- Production deploy identity scoped independently to Production resources;
- observability reader identity scoped to the approved Log Analytics workspace, Application Insights resource, and Container Apps read operations;
- no subscription Owner or broad Contributor assignment.

### Secret management

Use Azure Key Vault and GitHub Environment secrets or variables as appropriate. Module 083 stores only secret reference names and readiness state. It never stores or returns secret values.

### Observability

Provide the resource identifiers for:

- Application Insights;
- Log Analytics workspace;
- Azure Container Apps API and web applications;
- SLO and alert definitions;
- release/source marker location;
- approved log queries with sensitive-field suppression.

### Notifications and issue tracking

Module 065 and Module 076 can be the Pulse systems of record. Teams, email, ServiceNow, Jira, or GitHub Issues are optional adapters and should not be required for the first autonomous Test path.

## What can be built before access is supplied

The following source can be completed with all adapters disabled or in dry-run mode:

- policy and release-manifest schemas;
- durable orchestration contracts;
- adapter interfaces and readiness model;
- dry-run GitHub, deployment, observability, notification, and issue adapters;
- automation configuration and kill-switch UI;
- policy simulation and explainability;
- Test runbook generation;
- evidence normalization;
- validator and CI coverage;
- threat model, permissions matrix, recovery plan, and operating guide.

No external credential is required for that foundation.

## Activation sequence

1. Merge the source-only autonomous-control-plane foundation.
2. Apply its additive database migration to Test.
3. Validate dry-run orchestration and policy decisions in Module 083.
4. Register the read-only GitHub and observability adapters.
5. Enable issue and notification writes.
6. Enable autonomous CI and disposable canaries.
7. Enable protected Test deployment and automatic Test rollback.
8. Operate and measure the Test path.
9. Add Production manifest preparation and approval requests.
10. Enable protected Production deployment only after security, operations, and release-management acceptance.

## Non-goals of the foundation PR

The foundation PR must not:

- merge or modify PR #600;
- deploy to Test or Production;
- apply a database migration;
- create or change Azure resources;
- create or reveal secrets;
- activate GitHub, cloud, or external AI writes;
- weaken Module 077 fail-closed controls;
- bypass protected GitHub Environment approvals;
- make a public repository from private Pulse source.
