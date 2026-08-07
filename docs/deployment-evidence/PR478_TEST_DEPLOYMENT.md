# PR #478 Test Deployment Evidence

- Pull request: [#478](https://github.com/ahmedadeyemi-cts/project-time-platform/pull/478)
- Exact application release: `8e0bd5ae1dbdd41d1c57b3b2aea69fd9bb4457ca`
- One-time deployment controller: `6da9cd6d21f0a3fcbebc134b99a78bcbf058f60c`
- Successful deployment run: [30960388475](https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/30960388475)
- Successful deployment job: [92162816029](https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/30960388475/job/92162816029)
- Deployment artifact: [8912761058](https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/30960388475/artifacts/8912761058)
- Artifact digest: `sha256:b4f9e114e9a5bc3b8667280274f0975c33a7971ef78ab8946e228a9ce74d4226`
- Environment: **Test**
- Conclusion: **success**
- Production changed: **No**
- API changed: **No**
- Database or migrations changed: **No**

## Immutable web release

- Previous web image: `acrphdtest7825cc.azurecr.io/project-health-dashboard-web@sha256:52269ba8797a1cbaa0279b59bb156198d33446fd101631d06c2eb0beda5584ef`
- Previous web revision: `ca-phd-test-web-westus3--u477web-30957382960-1`
- Active web image: `acrphdtest7825cc.azurecr.io/project-health-dashboard-web@sha256:4f761298bc880ea7ad2cb235f2da943038d23d299fc4977c04ef0fd47342eb92`
- Active web revision: `ca-phd-test-web-westus3--u478web-30960388475-1`
- Revision mode: **single**
- Active digest verified: **Yes**
- Rollback required: **No**

## Verified behavior

- The More launcher uses the shared module registry as its label source.
- Module 001 renders as **Timesheet**.
- Module 002 renders as **Approval Inbox**.
- Compatibility aliases are consolidated after role and permission filtering.
- Dynamic RBAC and View-As permission evidence remains fail-closed.
- The live no-cache bundle was inspected recursively, including the lazy-loaded More stylesheet.
- One JavaScript asset and two stylesheets were verified from the served Test revision.
- ProjectPulse CI, repository security, and the Test release controller all completed successfully.

Test URL: [Pulse — Test](https://phd-west-test.onenecklab.com)
