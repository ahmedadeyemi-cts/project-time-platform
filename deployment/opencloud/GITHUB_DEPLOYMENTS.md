# GitHub ownership, package delivery and deployment handoff

## Existing source relationship — preserve it

Personal source: `ahmedadeyemi-cts/project-time-platform`.
Corporate destination: `US-Signal-Technical-Operations-Center/ProjectPulse`.

The existing `mirror-to-us-signal-projectpulse.yml` advances corporate `main` by fast-forward and copies selected release tags. It refuses divergent corporate history. Keep packaging source changes in the current PR path until an explicit ownership transition. Do not write a separate corporate main commit while treating the destination as a one-way mirror. Unmerged feature-branch files are not yet replicated by that main/tag mirror.

The mirror is source/tag replication, not replication of Actions environment secrets, runners, repository protections, image packages, GitHub Release assets, or deployment receipts. This kit does not change it. The last direct corporate access attempt was blocked by organization SAML enforcement; resolve that via the approved company SSO/application authorization process, never by forwarding a token in a package.

## Proposed ownership after installation

Keep development source personal for now, then mirror its reviewed commit. Prefer company-owned image publication and OpenCloud deployment authority. Decide exactly one repository/controller for each OpenCloud environment. Builds and tests may run elsewhere, but two controllers must not independently deploy to the same target. A lock must coordinate by environment across repositories, not assume identical GitHub concurrency strings create a cross-repository lock.

The recent `.verity/SHIPYARD.md` and release workflow already describe a web/API image release scaffold. This PR reuses the canonical app Dockerfiles and does not activate, retag or overwrite that scaffold. Review its registry ownership, tag triggers, source-identity handling, scan/publication order and database-baseline assumptions before adopting it for OpenCloud. Mirror-triggered work-repository events must not accidentally start a second publisher or legacy Azure deployment. Do not create a release tag merely to test this handoff.

## Package distribution

Place reviewed packaging source under `deployment/opencloud/`. Publish its generated configuration ZIPs and SHA256SUMS as versioned release assets after approval. Host actual application images in an approved private registry with the source commit and manifest digests recorded. A normal repository collaborator cannot be restricted to one folder; use corporate repository/team permissions or an approved restricted release-delivery arrangement.

The current personal source repository is public at assessment time. Never commit credentials, private certificates, operational database copies, installed worker secrets or customer attachments. Large images/model weights belong in the approved registry/asset distribution, not Git commits. OneDrive can carry a byte-identical handoff copy; recipients should verify its checksums and source revision against the authoritative release.

## Deployment connection

Normal users use internet HTTPS, without VPN. Human SSH uses VPN/management access. A restricted company self-hosted Actions runner can receive jobs over outbound HTTPS and reach the target hosts using the approved management path. Outbound access to github.com alone is not sufficient: configure required Actions/artifact/registry endpoints, registration, repository scope and host deployment permissions.

Do not expose public SSH or the Docker API for convenience. Do not give an untrusted pull-request job access to the deployment runner, live secrets or a Docker socket on production. Prefer isolated build runners and a tightly scoped, approved-release-only deployment runner. Confirm organization policy and the GitHub plan's available environment protections.

## Required deploy contract, not yet an activated workflow

1. Select an exact reviewed source commit and approved image-manifest digests. Verify that source exists in corporate history and that build/test/scan evidence refers to the actual images selected.
2. Obtain the environment-scoped approval and target-wide lock. Use company-owned scoped credentials; verify host identity over the approved management connection.
3. Save an installation manifest and recovery evidence. Apply only the baseline-specific reviewed migration plan; never glob and replay the full SQL directory.
4. Stage the containers without destructive volume operations. For Celar, recreate namespace-sharing services as one coordinated unit. Protect scheduled work and billing from multiple active writers.
5. Verify actual HTTPS identity, auth, API/web version and business acceptance. Report Passed/Failed/Skipped/Blocked separately, with the exact installed release.
6. On failure, restore a compatible prior application release or execute the explicitly approved data recovery plan; preserve logs and audit evidence.

This PR runs read-only packaging CI on GitHub-hosted runners. It does not grant deployment privileges, publish images, open a corporate runner connection, alter Azure/Oracle controllers, or start an OpenCloud rollout.

## Primary references

Docker Compose startup dependencies: https://docs.docker.com/compose/how-tos/startup-order/
Docker persistent volumes: https://docs.docker.com/engine/storage/volumes/
Docker secret handling: https://docs.docker.com/compose/how-tos/use-secrets/
GitHub self-hosted runner security: https://docs.github.com/en/actions/reference/security/secure-use
GitHub SAML app authorization: https://docs.github.com/en/enterprise-cloud@latest/authentication/authenticating-with-single-sign-on/authorizing-an-app-for-single-sign-on
Caddy explicit certificate configuration: https://caddyserver.com/docs/caddyfile/directives/tls
