# Module 025 — SOW/GSD lifecycle and SELL processing

## Release boundary

Dedicated feature branch based on main `bf401fa1d017eae0ebf10c9ed79720829ce8de60`.
Do not merge, deploy, alter the frozen FlowHive candidate, publish customer documents,
or send real email as part of source development. Protected UAT/Test acceptance is
required before declaring the complete workflow available.

## Product contract

1. Keep the existing immutable `engagement_id` / `SOW-YYYY-######` as the single
   logical package identity. Create the register record when the workspace package
   is created, not on first download or first SELL submission.
2. Retain every successful AI generation as append-only evidence, including the
   original SA attribution and saved source revision. Failed generation attempts
   must not be counted as successfully generated SOWs.
3. A saved document-content change creates a new immutable published version under
   the same identity. Downloads and retry clicks must not create another package,
   another identical content version, another SELL deal, or another notification.
   Editing the working copy never rewrites a prior generated/published snapshot.
4. Export the SA-reviewed saved content as SOW DOCX and GSD XLSX, retaining existing
   standard/Toyota/Hyundai template routing. Preserve the exact exported bytes and
   SHA-256 per published version. Both files must refer to the same saved snapshot.
5. Provide a dedicated SOW register / SELL history tab inside the SOW Generator:
   one row per package; expandable versions; generation, download, submission,
   acknowledgement and notification history; customer, SA, AE and Inside Sales;
   timestamps, version and external record reference; authorized reporting/export.
6. An authorized non-impersonating SA may submit their own reviewed package to SELL.
   Administrative support and manager read scope must follow existing Module 025
   authorization; managers are not implicitly granted write or cross-team access.
7. SELL submission requires saved, reviewed content, all three active internal
   recipients, an unambiguous customer linkage, and a usable approved connector.
   Block stale revisions, missing recipients, disabled/unconfigured connectors,
   invalid document pairs and unauthorized requests before external side effects.
8. Maintain one external SELL parent record per logical SOW. A revised submission
   attaches/version-links both files to that parent. A submission's immutable
   acknowledgement binds external parent ID, both document IDs and hashes to the
   exact internal version. Never equate queued, HTTP-requested, partial-upload or
   uncertain-outcome work with confirmed SELL success.
9. Use durable submission and notification outboxes with stable idempotency keys.
   Concurrent clicks/retries are deduplicated at the database, not only in React.
   An unknown remote outcome requires reconciliation, not blind record recreation.
   A notification retry must never recreate/reupload the SELL record.
10. Queue one notification for each successfully acknowledged version, only after
    SELL confirms the parent record and both documents. Deduplicate recipients by
    normalized address while retaining their roles in the evidence. Resolve actual
    addresses server-side; never accept arbitrary recipient addresses from clients.
    Revisions use updated/version language, not a second claim of record creation.
11. Honor existing environment and recipient safety controls. UAT uses isolated
    SELL fixtures/sandbox and an approved email sink; customer publication and real
    notification are disabled by default. A test fixture is never production proof.
12. Retain evidence on archive, failed submission and rollback. Application-level
    immutability includes database UPDATE/DELETE/TRUNCATE rejection on evidence;
    a database superuser is outside that trust boundary. Do not claim WORM storage
    or cryptographic tamper-proofing merely because rows contain hashes.

## Notification wording

Subject (first acknowledged version):
`SELL record created — {Customer Name} — {SOW Number} v{Version}`

To: assigned Inside Sales Representative
Cc: assigned Account Executive and Solution Architect (deduplicated)

`{Solution Architect Name} has uploaded a SOW and GSD in SELL for customer
"{Customer Name}". {Inside Sales Representative Name}, please assist in
processing this quote.`

Include SOW number, submitted version, upload time, SELL record reference/link,
and authenticated links to the exact version's SOW and GSD. Subsequent versions
say `SOW/GSD updated in SELL` and ask Inside Sales to process the revised quote.
Do not include credentials, public document links or invented commercial totals.

## Reporting definitions

- Packages created: distinct immutable engagement IDs created in the period.
- SOWs generated: distinct engagement IDs with a successful generation in the period.
- Generation runs: successful generation events, separately from unique packages.
- SOWs sent for processing: distinct engagement IDs with an acknowledged complete
  SELL submission in the period, not queued requests or downloads.
- Submitted versions: distinct acknowledged (engagement ID, version) pairs.
- First submissions and revisions: reported separately; downloads never inflate
  creation or submission metrics.
- Filter by authorized SA/team scope, customer, status and a half-open UTC interval.
  Archived packages remain reportable. Historic snapshots that were never retained
  must be labeled unavailable, not reconstructed and presented as historical truth.

## Acceptance evidence required

- [ ] Real PostgreSQL migration/reapply and retained-evidence rollback tests.
- [ ] Permanent ID at creation and persistence for never-downloaded generated work.
- [ ] At least 50 repeated/concurrent downloads produce one package and one version
      for unchanged content, with byte-identical SOW/GSD artifacts.
- [ ] Saved SA edits appear in both exported documents; unsaved/stale data rejected.
- [ ] Revised scope produces v2 under the same internal and external parent IDs;
      v1 remains downloadable and immutable.
- [ ] Double-click, retry, parallel-request and crash/recovery deduplication tests.
- [ ] Failure after parent creation or one upload remains incomplete and recoverable;
      no success email, no success count, no duplicate external parent.
- [ ] Exact recipient resolution, HTML/text escaping, email content and sink tests.
- [ ] Email failure/retry does not repeat the SELL submission.
- [ ] SA ownership, manager/team scope, impersonation, CSRF and negative access tests.
- [ ] Independent count reconciliation including archived records and date boundaries.
- [ ] Actual React browser tests for selection changes, stale responses, version
      downloads, register refresh and actionable connector/notification errors.
- [ ] Full backend/frontend build and existing Module 025 regression checks.
- [ ] Protected UAT end-to-end evidence from the exact reviewed PR commit.

Existing Module 026 source identifies its SELL provider as `zendesk_sell` and uses
its encrypted credential store. Outbound record/document creation must be verified
against that provider's actual API contract; inbound import success alone does not
prove outbound upload capability.
