# Preparation and cutover checklist

Target: approximately 2–3 months after this foundation. All items begin **pending**. Assign a named owner, target date and evidence link in the release tracker. Local tests or a green CI run do not count as live business acceptance.

## Now: establish the baseline

- [ ] Current Protected UAT deployment completes; record installed API/web/source identities before scheduling the next release.
- [ ] Merge this foundation only after current-main compatibility and CI are reviewed.
- [ ] Engineering/DBA classify the initialization catalog and identify mixed schema/demo scripts, runtime bootstrap inserts and migration dependencies.
- [ ] PMO, Finance and Sales operations approve the configuration inventory and authoritative sources for real users/customers.
- [ ] Operations document actual Test database, storage, queue, secret-store and RAG identifiers. Inventory all mounts/buckets, not just the primary attachment store.
- [ ] Define RPO/RTO, support hours, load expectations, retention and recovery owners.

## Next 30 days: prove the business workflow

- [ ] Exercise SOW/GSD → project creation → resource assignment → time entry → Manager/PM/PTC approval → expenses/billing → closeout.
- [ ] Test Engineer, Engineering Lead, PM, PM Lead, SA, AE, PTC and Administrator accounts; include denied cross-project reads/writes and user preview.
- [ ] Validate real file formats, download persistence after restarts, task/hour reconciliation and approval correction/resubmission.
- [ ] Test autosave recovery, notification routing, duplicate suppression and partial failure handling using controlled recipients.
- [ ] Record performance at expected project/document/time-entry volume, concurrent sessions and AI generation load.
- [ ] Define the real Production deployment and rollback process; the existing Production workflow is a placeholder.

## Next 30–60 days: rehearse the clean environment

- [ ] Build a disposable isolated environment from an empty database using the reviewed initialization plan. No Test database clone.
- [ ] Verify required schema/reference rows and migration ledger; no demo users, sample projects or unexpected assignments. Keep evidence of every executed script/version.
- [ ] Prove actual production namespace separation and least-privilege credentials across database, files, queues, secrets and Celar retrieval.
- [ ] Configure production SSO and secret references; verify approved users and recovery access. Do not copy Test secret values or local password hashes.
- [ ] Keep actual outbound email, external writes and workers disabled until validation. The foundation JSON flags alone do not enforce this.
- [ ] Restore a database **and matching documents** into an isolated environment, verify downloads and measured recovery objectives.
- [ ] Test monitoring and alert delivery for app/database health, backup failure, storage, queue backlog, email failure, integration failure, provider outage and secret expiry.
- [ ] Rebuild RAG from approved sources; verify a known UAT-only canary cannot be retrieved by Production and denied records remain denied.
- [ ] Verify a fresh installation again after subsequent schema changes. An earlier successful rehearsal does not qualify a later release automatically.

## Final 2–3 weeks: cutover rehearsal

- [ ] Record exact release SHA and image identities, database/schema version, source-data export versions and document templates.
- [ ] Run the timed installation/import/validation sequence with assigned owners and escalation contacts.
- [ ] Define change freeze, backup checkpoint, final source synchronization, rollback decision thresholds and business sign-off.
- [ ] Reconcile imported customers/users/reference records and any separately approved opening business data.
- [ ] Check clean notification and integration queues before enabling workers; no Test retry jobs or sync cursors.
- [ ] Verify supported browsers, mobile layouts, keyboard access and operational training.
- [ ] Confirm Test and Production hostnames, labels, SSO redirects, recipients and external-account identities.

## Go-live and early support

- [ ] Use the reviewed Production deployment process and its existing environment approval controls.
- [ ] Verify installed source/API/web identities and role-based business workflow on Production.
- [ ] Enable outbound integrations, notifications and workers in the documented sequence; verify controlled first transactions.
- [ ] Monitor business totals, failures and performance through the agreed support period.
- [ ] If rollback is required, stop new writes/delivery, assess post-cutover transactions and use the rehearsed application/data recovery plan. Reverting code alone may not reverse schema or external side effects.
- [ ] Retain UAT and its backups until the agreed retirement date. Any later deletion is a separate scoped action.
