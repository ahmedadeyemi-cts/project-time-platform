# PR #55 Test Deployment and Browser Verification

This runbook covers the one-time guarded test deployment of the verified PR #55
merge commit:

`ea23da6cfdd21a9444489ee4ffd14a6555de8c34`

It does not deploy production, configure credentials, connect a CRM/ERP provider,
or test an external provider.

## Before running the workflow

The GitHub `test` environment must contain the secret
`PROJECTPULSE_TEST_DATABASE_URL`. Store the PostgreSQL connection URI only in the
GitHub environment secret. Do not place it in source, workflow inputs, logs, or
chat.

The existing test-environment Azure variables must also remain configured:

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

The workflow stops before changing either application if the commit, migration
checksums, database prerequisites, container builds, or atomic migration
transaction fails. After deployment starts, a failed health or smoke check
restores both previously captured application images. Migrations 034 and 035 are
additive and remain in place after a successful transaction.

Both Container Apps must already use single-revision mode. The workflow builds
commit-specific tags, resolves their registry digests, and deploys the immutable
digest references so a mutable tag cannot change the tested release.

## What the workflow verifies automatically

- The release source is exactly the PR #55 merge commit.
- Migration 034 and migration 035 match the reviewed SHA-256 checksums.
- Both migrations apply in one PostgreSQL transaction.
- The Module 026 audit constraint accepts module `026`.
- The Work Register `source_mode` and audit foreign-key contracts exist.
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
4. Module 055C is named **Manage Existing Projects** and is accessible to the
   expected PM and Project Team Coordinator roles.
5. Module 055D is named **Create New Project** and is restricted to the Project
   Team Coordinator role.
6. Module 026 opens without a schema error. Do not enter credentials or run a
   provider connection test during this release verification.
7. In Module 055D, use test-only records to verify that the GSD and SELL intake
   choices load and that a controlled test project can be created.
8. Return to Module 055C and confirm the test project can be located and opened.

Record any ordering, access, label, or workflow problem before beginning the
separate 055C closeout and partial-invoice implementation.
