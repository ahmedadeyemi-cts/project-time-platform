# PR #477 Test Deployment Evidence

- Pull request: [#477](https://github.com/ahmedadeyemi-cts/project-time-platform/pull/477)
- Application SHA: `6f2b5ef459e1a8efa234a9efb1dc14e1fc56379b`
- Deployment controller head: `e5781cb23efe9fc47a0d798db5d99bacc46bb3f6`
- Successful workflow run: [30957382960](https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/30957382960)
- Successful job: [92153541053](https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/30957382960/job/92153541053)
- Deployment artifact: [8911747868](https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/30957382960/artifacts/8911747868)
- Artifact digest: `sha256:2be7bcdac676d21d643f3b3c01f84ea0fc46418c96b4b7cca89b12d760066e66`
- Conclusion: **success**
- Environment: **Test**
- Production changed: **No**
- Migration: `069_module006_customer_pipeline_expansion`

## Immutable release outputs

- Migration image: `acrphdtest7825cc.azurecr.io/project-health-dashboard-uat477-migrator@sha256:1ebf4fc7b2f718de64869b350631d2f240bdbcba4d7ef35a3bf5aed85a3c765f`
- API image: `acrphdtest7825cc.azurecr.io/project-health-dashboard-api@sha256:6298ea5a9dea83dd0f527e666ec0aff8b3378f17d5584e2c805da0a28985e8f4`
- Web image: `acrphdtest7825cc.azurecr.io/project-health-dashboard-web@sha256:52269ba8797a1cbaa0279b59bb156198d33446fd101631d06c2eb0beda5584ef`
- API revision: `ca-phd-test-api-westus3--u477api-30957382960-1`
- Web revision: `ca-phd-test-web-westus3--u477web-30957382960-1`
- Approved stacked-logo SHA-256: `f28a48b72d16d5a2d0377d559ba0a549f4486309cc6e09a285a32840e0df806b`

## Verified release behavior

- Migration 068 prerequisite and Migration 069 idempotent apply passed.
- Module 006 records, updates, tasks, and task events retained their counts and hashes.
- API health, release-source SHA, and protected Module 006 routes passed.
- The no-cache served bundle contains extensible customers, a one-line Status header, and one status indicator per row.
- Module 005 upload-history layout and its visible **Accept as PM** action passed.
- Module 065 action parity, the permission-scoped More launcher, and the served approved stacked-logo bytes passed.
- The deployment evidence artifact was uploaded successfully.

## Superseded controller attempt

Run [30956464226](https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/30956464226) correctly failed closed because its served-asset verifier did not load the More launcher's lazy stylesheet. Its application-image rollback step completed successfully. The controller-only correction added lazy-CSS discovery; the successful run above then reapplied the backwards-compatible migration idempotently and deployed the exact release.
