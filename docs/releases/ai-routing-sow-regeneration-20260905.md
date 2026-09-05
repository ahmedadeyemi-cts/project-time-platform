# AI provider routing and detailed SOW regeneration repair

Boundary: application repair for Protected Test. Oracle runtime PR #838 is separate.
Production deployment is not included.

The configured route remains DeepSeek v4, Celar AI, Claude, OpenAI, then the governed
local fallback, subject to saved priorities and existing evidence/privacy policies.

## Repairs

- Keep the deployment-level private RAG switch independent of the Celar provider's
  enabled flag. Disabling Celar clears its inference connection but does not disable
  shared authorized evidence execution for DeepSeek.
- Accept a successful DeepSeek document-grounded timesheet response and attribute
  DeepSeek composition correctly.
- Preserve specific skip reasons and check Celar configuration/circuit state before
  invoking the consumer. Direct private-model consumers also consult provider health.
- Failed readiness opens the bounded cooldown; a successful recovery probe restores
  availability. No readiness check can guarantee the next network request succeeds.
- Reserve 2,048 reasoning tokens in addition to the DeepSeek consumer's final-answer
  allowance, capped at 16,384. Timesheets previously requested only 520 tokens total.
- Require a verified autosave before generation so the current Service Overview is
  used. Regeneration of a generated editable draft remains supported. Confirmed SOWs
  retain their existing Reopen requirement and manual final-hour edits remain intact.
- Remove the Oracle transport's forced ten-package and single-item-list compression.
  Bounded 12,000/10,000-token primary/recovery requests retain timeouts, strict schema,
  all five phases, citations, detailed steps, and positive recommended hours.

## Acceptance still required in Protected Test

1. With a saved, enabled DeepSeek key, generate a factual timesheet; inspect provider
   attribution and route decisions, including a document-grounded example.
2. Disable Celar only, keep private RAG enabled, and verify DeepSeek SOW and planner
   generation from authorized evidence.
3. Simulate provider unavailability through supported Test controls; verify the next
   eligible provider and explicit skip codes, then verify recovery.
4. Generate and regenerate a multi-technology SOW; inspect all five phase sections,
   steps, deliverables, assumptions, recommended hours, and manual-hour preservation.
5. Verify failed autosave does not launch generation from a stale Service Overview.

Public providers still receive only the existing approved sanitized capsules. They
cannot replace missing private evidence with uncited project scope. Scanner, OCR,
embedding, identity, refusal, and citation policies are not weakened by this repair.
