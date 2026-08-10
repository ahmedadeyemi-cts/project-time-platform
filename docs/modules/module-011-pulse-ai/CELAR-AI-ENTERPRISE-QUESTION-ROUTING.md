# Celar AI enterprise question routing and privacy boundary

Celar AI uses one private-first routing authority for Module 011, Module 064, and every AI-enabled consumer. The default target order remains:

1. private Celar AI and governed Pulse tools;
2. Claude when the question is eligible and the prior answer is unavailable or below the public-answer confidence floor;
3. OpenAI when Claude is unavailable or below that floor; and
4. the governed local answer.

A safety refusal stops routing. It is never bypassed by a later provider.

## Public organization facts

Company-specific public questions are eligible only when the organization is present in the deployment-owned public entity allowlist and the question contains no Pulse, customer, employee, project, document, financial, account, ticket, or internal-system context. The built-in registry includes US Signal and commonly referenced public technology providers. Additional names use:

`PROJECTPULSE_CELAR_AI_PUBLIC_ENTITY_ALLOWLIST`

This is a public-entity exception, not a general proper-name exception. Ambiguous names remain private and fail closed.

## External data-loss prevention

Claude and OpenAI receive the original question only through the isolated public-question path. Every other external route receives a closed backend-owned purpose capsule. Raw SOW/GSD text, attachments, retrieved chunks, customer or employee identities, project records, financial values, credentials, internal hosts, and identifiers remain inside Pulse.

Provider output is checked again before display. A response that reintroduces protected terms, credentials, identifiers, or unsafe named entities is rejected. Public answers that are semantic non-answers, extremely short, or contain governed low-confidence signals are not promoted; routing continues to the next approved provider.

## Runtime activation

The protected Test activation controller uses Module 064 authorization for the private-model probe and Module 011 authorization for the document-runtime readiness endpoints. Failure evidence contains only allowlisted status and diagnostic fields; endpoint URLs and credentials are never returned.

## Theme contract

The final shared stylesheet is loaded after legacy module styles. It enforces semantic surfaces, controls, borders, text, muted text, status chips, focus indicators, and visible Light/Dark controls across Module 064, Celar AI, shared enterprise panels, and standard routed modules.
