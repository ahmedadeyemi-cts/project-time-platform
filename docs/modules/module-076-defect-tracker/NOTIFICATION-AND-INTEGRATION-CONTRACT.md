# Module 076 Notification and Integration Contract

## ProjectPulse Help

The global Help assistant includes **Report a defect — Module 076**. It opens `?defectSource=help#defect-tracker`, allowing the center to preserve Help as the intake source without browser storage or a direct mutation.

## GitHub

`.github/ISSUE_TEMPLATE/projectpulse-defect.yml` provides the governed issue shape and assigns new template-created issues to `ahmedadeyemi-cts`. The future synchronization adapter must use a GitHub App or signed webhook and must enforce:

- exact repository/installation allowlisting;
- signature verification before payload processing;
- bounded request size and rate limits;
- delivery-ID and issue-node-ID idempotency;
- actor, app, repository, event, and timestamp evidence;
- stable GitHub issue-to-defect linking;
- loop prevention for ProjectPulse-originated updates;
- sanitized logs and responses; and
- replay and reconciliation tests.

Claude and ChatGPT report **through GitHub**. Module 076 does not call either provider. Trusted AI source attribution comes only from reviewed GitHub App or bot actor metadata; issue text cannot self-declare a trusted source. Any future AI classification, summarization, or duplicate detection must use Module 064 shared routing (Claude first, OpenAI second, governed local fallback) and remain human-reviewed.

## Email

| Event | Audience | Required atomic event | Deduplication key |
|---|---|---|---|
| Defect opened | Active managers | Defect creation + outbox event | `defect_opened:{defectId}` |
| Defect resolved | Original reporter | Resolution transition + outbox event | `defect_resolved:{defectId}:{resolutionVersion}` |

Module 067 Global Mail owns delivery, retries, dead-letter behavior, recipient safety, and provider configuration. Module 076 owns message intent and defect-specific content only. No Brevo-specific, SMTP-specific, Microsoft Graph, Cloudflare, or provider secret is stored here.

The current package does not write the outbox or send a message. Email activation requires database and Module 067 delivery authorization.
