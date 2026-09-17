# Module 025 external phase contract

## Evidence

Protected Test qualification run [35237329449](https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/35237329449), job 105256516051, failed on 2026-09-17 at 15:06:59 UTC on merged commit f30b4e3a8cc7ba8daba4cad001e5dca84f7140f6. The artifact reports OpenAI model gpt-5.6-luna completed in 58.342 seconds with 983 input tokens, 6,185 output tokens (162 reasoning), and 36,972 output characters. Its result was rejected with module025_external_phase_contract_invalid. The diagnostic only retained phase_contract_or_json and no field. The rejected response was not retained, so this change does not claim to reconstruct which field failed that run.

Artifact 10504566102 SHA256: 5ce70b8e34a1b16b9e5267f35ff0c6053da87e85a60475dbc04a88552f683a16. API revision and template were unchanged before build, before inference, and after cleanup. Temporary qualification job cleanup was verified. No application deployment or full lifecycle acceptance occurred.

## Confirmed implementation defect and correction

The external prompt referred to an internal plan type without its complete schema and broadly called list fields arrays of strings, although milestones require objects with name, description, proposedTiming, acceptanceEvidence, citationIds and isAssumption. The OpenAI Responses request had no structured output format. This is a verified contract mismatch, not proof of the exact rejected field in the lost response.

The external adapter now supplies the full canonical schema and unambiguous milestone instructions. OpenAI receives text.format=json_schema, strict=true, using the server-selected phase. The schema covers every plan, task and milestone property, with required keys and additionalProperties=false, positive effort, requested phase, detailed fields and citation shape. Optional collections can be empty; unknown customer facts remain explicit questions. The task count remains scope-driven with a minimum of two, without a two-task ceiling. The existing parser still applies distinct-outcome, step, deliverable and non-boilerplate checks. Valid legacy phase shapes remain supported by that parser.

Safe diagnostics distinguish JSON syntax, task details, milestone details, wrong phases, duplicate WBS and semantic phase failures. Diagnostic-only schema traversal resolves structural errors after rejection. It never records response values or unrecognized property names. Qualification logs print allowlisted categories and field paths. Rejected text stays discarded.

The OpenAI request format follows the [official Structured Outputs documentation](https://developers.openai.com/api/docs/guides/structured-outputs). Model selection, configuration, 12,288-token cloud allowance, 120-second provider deadline, durable request budget, refusal handling and single-attempt transport are unchanged. Claude receives the same explicit schema in the prompt; this change does not assert Claude transport-level constrained decoding. Private provider behavior and FlowHive prompts are unchanged.

## Validation and release limits

Regression coverage uses the real closed-capsule adapter, OpenAI HTTP request builder and phase parser. It verifies all five detailed phases, schema coverage of every DTO field, preservation of steps/acceptance/effort on assembly, rejected milestone strings and incomplete milestone objects, missing acceptance/input detail, numeric list entries, wrong phases, duplicate WBS, repeated outcomes, prohibited boilerplate, malformed JSON and safe diagnostics. Existing privacy, refusal, incomplete response, durable checkpoint and full-document retention tests remain required. No live model call is part of these tests.

Exact source-scope checks preserve deployment authority, native Test approval, deployment workflows, Production, database, credentials and private runtime configuration. Passing these tests does not establish provider qualification or live SOW/GSD lifecycle success. A fresh one-phase qualification against the merged correction must pass before full Test lifecycle verification. SELL publication still requires the supported document-write adapter and verified receipts; this repair does not implement that adapter.
