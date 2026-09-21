# FlowHive and SOW/GSD generation repair

## Observed UAT failures

Source `219fad3923da2e08cdd1a84d54031d9dad76bf64` deploys successfully, but deployment health does not prove generation works. A fresh, isolated CUCM 14.0-to-15.0 SOW test reproduces private-provider timeout behavior in Plan. The phase timer works; completion is still unproven.

FlowHive phase calls stop at 330 seconds, but previously omitted the gateway workload/deadline headers and could receive a legacy 3000-second first attempt. The actual Python route reproduces this mismatch. Its private phase prompt also named an internal schema without supplying its exact field shape.

DeepSeek planning requests omitted reasoning effort, and readiness probes reserved thousands of completion tokens for a greeting. DeepSeek's documented default is high thinking effort. The repair requests low effort plus JSON output for SOW/FlowHive, gives durable phases their explicit total token ceiling, and uses a non-thinking 32-token readiness probe. Support and performance on the installed DGX endpoint still require live verification. Reference: https://api-docs.deepseek.com/guides/thinking_mode/

## Changes and safeguards

- FlowHive sends a feature-bound phase workload and a 300-second gateway deadline, below the 330-second caller budget. The gateway performs one local attempt for each durable phase and rejects mismatched feature/workload pairs and invalid deadlines.
- The FlowHive phase prompt includes the complete task JSON contract, real source citations, positive effort/duration, responsibilities, acceptance, and validation.
- Gateway release 1.1.9 couples the runtime change to the existing version preflight before API deployment.
- Private evidence restrictions, external-provider approval, refusal handling, full task validation, durable checkpoints, and protected deployment authority remain intact.

## Separate environment prerequisites

Module 064 currently reports that UAT uses a legacy temporary upload root, has no verified shared persistent mount, has automatic document admission disabled, and has zero ready SOW/GSD documents. FlowHive cannot generate a document-grounded WBS until these are resolved. Do not mark a temporary directory persistent or enable admission without the existing dedicated service-principal and document-permission checks.

Claude and OpenAI are available, Gemini is rate-limited, and the saved SOW route has not approved sanitized paid generation. FlowHive's detailed WBS currently requires private inference. This repair does not change those policies or represent an external fallback as a source-grounded plan.

## Acceptance

Regression checks exercise real private-client headers, gateway attempt deadlines, sequential phase assembly, citations, cancellation, checkpoint resume, and persisted Module 025 generation. Synthetic tests do not establish model quality or runtime capacity. Release completion requires live five-phase SOW/GSD output and a populated FlowHive WBS from ready documents, saved readback, and working timers.
