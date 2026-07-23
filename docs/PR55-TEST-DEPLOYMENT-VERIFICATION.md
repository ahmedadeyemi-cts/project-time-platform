# PR #55 Test Deployment and Browser Verification

This runbook covers the guarded test rollout of the verified Work Register
Work-to-Cash and role-aware welcome-page release commit:

`4cddc469f7bd20e4cb0e028e9ff1d47842ef7532`

It does not deploy production, configure credentials, connect a CRM/ERP provider,
or test an external provider.

## Before running the workflow

No separate GitHub database secret is required. After Azure login, the workflow
reads `PTP_DB_HOST`, `PTP_DB_PORT`, `PTP_DB_USER`, and `PTP_DB_NAME` from
the existing test API Container App, along with `PTP_DB_SSLMODE` when present.
It follows the `PTP_DB_PASSWORD` secret reference, masks sensitive values
immediately, URL-encodes them, and exports the PostgreSQL URI only to the current
runner's ephemeral environment.

The Azure identity used by the workflow must be able to read the test Container
App configuration and list its secrets. The workflow stops before migrations or
deployment if any value or permission is unavailable. Do not copy database
credentials into GitHub, source, workflow inputs, logs, or chat.
The existing test-environment Azure variables must remain configured:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_RESOURCE_GROUP`
- `AZURE_ACR_NAME`
- `AZURE_API_APP`
- `AZURE_WEB_APP`
- `PUBLIC_URL`

## Run the guarded deployment

1. Open **Actions** in GitHub.
2. Select **ProjectPulse Deploy PR55 Test**.
3. Select **Run workflow** from `main`.
4. Leave `release_commit` set to the complete verified SHA above.
5. Enter `DEPLOY-PR55-TO-TEST` in the confirmation field.
6. Start the workflow and wait for every step to pass.

The test PostgreSQL hostname is private and cannot be resolved by a
GitHub-hosted runner. The workflow therefore builds a dedicated migration image
whose context contains only the guarded migrator, migrations 034 through 039, the
migration Dockerfile, and the exact release-commit marker. It resolves that
image to an immutable ACR digest and runs it as a one-time manual Container Apps
Job in the same managed environment as the test API, where the linked private
DNS zone is available. The database URI exists on the temporary job only as an
Azure secret reference.

The workflow stops before changing either application if the commit, migration
checksums, database prerequisites, container builds, private migration job, or
atomic migration transaction fails. The job has no automatic retries, is
bounded to 15 minutes, and is deleted after success or failure by both a script
trap and an always-run workflow cleanup step. After application deployment
starts, a failed health or smoke check restores both previously captured
application images. Migrations 034 through 039 are additive and remain in place
after a successful transaction.

Both Container Apps must already use single-revision mode. The workflow builds
commit-specific tags, resolves their registry digests, and deploys the immutable
digest references so a mutable tag cannot change the tested release. It also
resolves both active rollback images to immutable digests before changing either
application.

## What the workflow verifies automatically

- The database connection is reconstructed from the existing test API Container
  App without exposing or duplicating credentials.
- The release source is exactly the verified Work Register rollout commit.
- Migrations 034 through 039 match the reviewed SHA-256 checksums.
- The migration image is resolved to an immutable digest in the approved ACR.
- The migration runs inside the test API's Container Apps environment and
  reaches the database through private DNS.
- All six migrations apply in one PostgreSQL transaction with retries disabled.
- The Module 026 audit constraint accepts module `026`.
- The Work Register `source_mode` and audit foreign-key contracts exist.
- Migration 036 is registered, the Administrator and Super Administrator roles
  hold the 055C edit and 055D create permissions, and the Work Register feature
  metadata is present.
- Migration 037 is registered; recognized contract variants are canonicalized to
  **Time and Material** or **Fixed Price**, and both 055C/055D date triggers exist.
- Migration 038 is registered; billing readiness, closeout, immutable lifecycle
  audit, void-safe source guards, and their required triggers/functions exist.
- Migration 039 is registered, and the deployed invoice-reactivation function
  acquires readiness-package advisory locks before time-entry locks.
- The temporary migration job and its database secret are removed before API
  deployment begins.
- The active API and web image references match the immutable release digests.
- The production frontend build reruns the module-ordering contract checks.
- `/health` and `/api/version` respond successfully.
- The Module 026 and Work Register routes are present and protected.
- The deployed frontend contains Module 055C, Module 055D, Module 999, and the
  names **Manage Existing Projects** and **Create New Project**.

## Browser verification

After the workflow succeeds, hard-refresh the test portal and verify the
following with your normal test accounts:

1. Dashboard cards and left navigation progress numerically from Module 001
   through Module 999.
2. Module 055B appears before 055C, and 055C appears before 055D.
3. Module 999 appears last.
4. Module 055C is named **Manage Existing Projects**. An assigned PM can edit
   the project, an unassigned PM is view-only, and Project Team Coordinator,
   Administrator, and Super Administrator can edit every project.
5. Module 055D is named **Create New Project** and is available only to Project
   Team Coordinator, Administrator, and Super Administrator.
6. Module 026 opens without a schema error. Do not enter credentials or run a
   provider connection test during this release verification.
7. In Module 055D, use test-only records to verify that the GSD and SELL intake
   choices load and that a controlled test project can be created.
8. Return to Module 055C and confirm the test project can be located and opened.

9. From a selected project in Module 055C, choose **Start Project Closeout** and
   confirm Module 040 opens with that project selected.
10. Confirm the welcome page shows role-appropriate actions, attention items,
    project health, assigned projects, billing snapshot, and recent activity.
    Engineering users should see time-entry content; Managers, Sales, Inside
    Sales, Executives, and Project Team Coordinators should not.
11. In Module 055C, edit and explicitly clear the SOW signed and estimated-end
    dates, then reopen the project and confirm the saved values remain correct.
    Confirm **T&M**, **TM**, and equivalent variants display as **Time and
    Material**, while GSD **FP** displays as **Fixed Price**.
12. In Module 039, save a readiness review for a defined billing period and
    confirm it uses verified invoice candidates and mapped Certify expenses only.
13. In Module 042, create a controlled partial invoice, verify the supplied US
    Signal logo appears in both PDF and Excel, void the invoice, and confirm the
    governed labor/package sources become available for replacement billing.
14. In Module 040, verify request, complete, and reopen. Confirm active tasks,
    pending time, billing readiness, invoice disposition, and confirmations
    block closeout when incomplete.
15. Return to Module 055C and confirm creation, edit, billing readiness, invoice,
    closeout, reopen, archive, and restore evidence appears in the Audit tab.

Record any welcome, ordering, access, persistence, billing, invoice, branding,
audit, or closeout problem before authorizing another rollout.
