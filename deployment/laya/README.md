# Laya document decisions: coordinated Protected UAT release

## Delivered scope

Module 064 -> authorized existing project document -> existing admission/extraction -> approved Celar HTTPS gateway -> resident Laya Unix socket -> typed recommendation -> retained accept/correct review.

This is an administrator-operated, human-reviewed document-classification capability, initially disabled. It is not a generative provider, whole-document analysis, approval, automatic document-category change, CRM/AP automation, or a repair for unrelated SOW/FlowHive failures. Existing generative provider sequence and dispatch are unchanged.

The host installation and restricted socket access have already passed on-host tests. Do not reinstall packages, redownload the model, or repeat manual socket preparation. The checkpoint remains `1c5edc17a7acd8701df6fc341c0d179f1c62c982`. The standalone worker's historic `integration_enabled: false` is not changed to manufacture a success indicator; the application reports its own actual gateway connectivity and Module 064 enabled state.

## Coordinated deployment

Merge the reviewed, passing PR into main under the existing deployment approval. The release has two managed paths, both consuming the committed code:

1. **Oracle gateway:** the existing Celar GitOps timer detects the changed Oracle subtree and invokes the exact main worktree's `deploy.sh`. For a strictly Laya-only delta, `incremental-policy.py` verifies the applied-tree record and byte-identical existing gateway baseline. Under the existing runtime mutation lock, deployment then calls `deployment/laya/deploy-gateway.sh apply`, starts the installed Laya service, installs the two adapters and the gateway-only service group/entrypoint drop-in, and tests real authenticated HTTP plus one synthetic invoice. It does not reinstall packages or restart Ollama, ClamAV, Caddy, or the firewall. Invalid baseline evidence stops the incremental rollout rather than silently reprovisioning. A fresh or genuinely broader Oracle update retains the original full provisioning path; a prepared host also receives the adapter afterward.
2. **Protected Test application:** the existing `module025-protected-uat-control.yml` controller dispatches the exact-main Test deployment using `sow_exports` acceptance. The existing retention migration image now includes the Laya schema, scoped grants and verification. It obtains the actual API database role from the Test application's explicit `PTP_DB_USER`, checksums the SQL and role binding with the release SHA, and uses the existing owned, private-network, Key Vault/TLS migration job. All previous migrations and their verification remain intact. No new privileged database runner or secret source is introduced. Laya SQL/grants must pass before the immutable API/frontend deploy. Evidence is written to `laya-database-migration.json`.

The protected deployment workflow and its admission/quarantine rules are unchanged. A merge that changes the retention runner already triggers the controller; do not create a second simultaneous deployment. A valid authorized controller comment can be used only when no matching automatic deployment was started.

The two managed paths may finish at different times. Keep the capability disabled until the Module 064 connection test succeeds. A successful API deployment alone does not prove Oracle rollout or live document accuracy. Do not stop Laya after successful cutover unless deliberately testing unavailability or ending the pilot.

## Application acceptance

In Protected UAT, open Module 064 and locate **Laya document classification** directly below the capability routing section. Select **Test gateway connection**, then **Enable classifications**, **Load documents**, choose an admitted project document and select **Classify document**. Record the accepted or corrected classification, refresh history, and confirm the original recommendation and review are retained.

Record the deployed commit, configuration version, authorized document ID/source hash, decision ID, reviewer identity/time, model revision and measured latency. Never include raw document text, tokens or credentials in operational logs.

Acceptance includes unauthorized/view-as denial; disabled capability causing no inference; valid document recommendation; persisted corrections; replacement/revocation invalidating access; unavailable/busy worker producing a clear review outcome without external fallback; and unchanged SOW/FlowHive provider sequence. Concurrent Ollama/Laya memory and latency remain on-host acceptance checks on the 12-GB server. Existing local smoke tests or synthetic CI cannot substitute for live acceptance.

## Security and input limits

Module 064 stores the setting in `celar_laya_settings`, with optimistic configuration version checks and retained audit. Only current active administrators can operate the pilot; view-as requests are rejected. Existing document scope, format, malware admission and native extraction apply. Test-only existing approved Oracle HTTPS configuration is required; candidate execution and production enablement are blocked.

The transport authorizes two fixed HTTPS paths on the existing approved host, reuses its protected bearer credential/privacy header and existing DNS/address validation, and retains TLS hostname verification. There are no configurable arbitrary URLs, proxies, redirects, retries or cloud fallbacks. The gateway loads no model; it uses `/run/celar-laya/decision.sock` under the dedicated service group with a total 12-second decision deadline, three-second health deadline, and bounded JSON.

The initial assessment uses the first 300 Unicode scalar characters of the first nonempty extracted section. Provenance includes the section index, named excerpt policy, source SHA-256 and excerpt SHA-256. The worker independently checks the exact 450-token state budget and rejects overflow. The UI explicitly labels this as excerpt-only, never a whole-document completeness assessment. Supported labels are SOW, invoice, purchase order and other. The raw model score is not a calibrated probability of correctness.

Classification rechecks document scope, source and policy after inference before persistence. Review rechecks the current source and preserves the original recommendation. No authoritative document category, approval, assignment or financial workflow is changed.

Audit tables have append-only update/delete triggers and scoped runtime-role grants. Database owners/superusers can administratively bypass triggers, so this is not immutable storage against a database owner. Existing enabled/disabled settings and history are preserved across migration reruns.

## Rollback

Disable classifications in Module 064. Roll back API/frontend with the existing release procedure. From the reviewed checkout on Celar, `sudo -n bash deployment/laya/deploy-gateway.sh rollback` removes only this adapter and gateway drop-in and restores the existing gateway entrypoint. Coordinate the desired main release with GitOps so reconciliation does not reapply an intentionally rolled-back adapter. Stop Laya only when intentionally releasing resources. Retain the additive audit schema; do not delete history or alter provider settings.

A failed first gateway apply attempts to restore its prior entrypoint and remove only files it created. Its returned result, not a preceding syntax marker, determines cutover success.

## Verification and ownership

`tests/laya/release-scope.py` is the exact additive branch registration for Module 064. It validates a closed file list and requires the original parent UI, endpoint registration, Oracle deploy body, existing migration verification and Module 064 workflow to remain unchanged outside named additions. Protected deployment/admission code, provider dispatch and Group 7 readiness ownership remain byte-equivalent to the base. This registration does not skip the existing Module 064 API/frontend/contract checks.

The dedicated Laya CI runs strict JSON/Unix/Flask tests, incremental-policy and release wiring checks, actual .NET contract code, ephemeral PostgreSQL migration/grant/append-only checks, real HTTP/database endpoint tests with synthetic session/document/transport substitutes, the actual API build, and the actual locked frontend production build with bundle markers. CI database credentials are generated per job and masked; no live secret or model download is used. Automated results and live deployment results must be reported separately.
