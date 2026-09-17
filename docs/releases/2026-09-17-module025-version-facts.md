# Module 025 requested version preservation

Qualification [35241653036](https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/35241653036) passed on 2026-09-17 at 15:46:47 UTC using commit 8a8673d607554158710b897e888beeaa3188f577. OpenAI gpt-5.6-luna produced three validated Plan work packages in 50.339 seconds (4,692 output tokens). API revision and template were unchanged, and cleanup was verified. This was provider qualification, not application deployment or full lifecycle acceptance.

Artifact 10505618883 SHA256: 6478228fcf8c5b88797fff31626f5e1906c708dc911f72eafa20d8ac271c91ac.

Review of the actual output found that SoftwareVersions and descriptions said the current and target releases were not supplied, although the synthetic source explicitly requested CUCM 14.0 to 15.0. The adapter's version regex rejected the sentence-ending period after 15.0, so neither release reached the provider. The failure is reproducible offline using the exact live qualification input.

This correction recognizes sentence punctuation while continuing to reject version suffixes, hostname fragments, overlong components and four-part addresses. Multiple transitions remain ambiguous and are not guessed. The exact qualification source is shared with the regression test, and the runner now blocks before inference if its required 14.0-to-15.0 transition is missing. Evidence records the requested transition and the input-preservation result.

Tests cover the exact live source, sentence-final and sentence-interior periods, other punctuation, end-of-input, numeric quantities, malformed version tokens, hostname fragments and multiple transitions. Existing complete phase, privacy, structured-output, checkpoint, document retention and browser tests remain required. No provider model, schema, inference budget, routing, private runtime, Production or deployment authority changes.

The schema qualification is a confirmed pass. This fix corrects fidelity of known input facts; it does not claim that all customer facts are now semantically verified. Live detailed-content review and the full normal-SA SOW/GSD and My Role lifecycle remain pending.
