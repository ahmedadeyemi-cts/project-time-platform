# Module 025 private generation recovery

Protected Test run 35420765273 installed a851956b6babaa085fe124ff17846bea922613e9 and passed application health and migration 111, then failed its first Plan phase. DeepSeek timed out at 120 seconds and Celar at 180 seconds. Paid fallback was disabled and remains disabled.

Host evidence supplied by the owner at 2026-09-19T04:52:02Z shows four CPUs, Gemma 3 4B loaded without VRAM, about 5.3 GiB available memory, no service restarts or OOM events and one Ollama timeout in the last day. It does not measure inference throughput, prove an idle host during the failed request, or establish that the new deadline will be sufficient.

The application previously cancelled Celar at 180 seconds while the Oracle gateway could continue inference within a 3,600-second SOW budget. This release gives a durable phase a 330-second application budget and sends an explicit 300-second gateway deadline. The gateway validates that ceiling and makes one selected local model attempt. Legacy non-phase requests retain their existing contract. The existing 120-second DeepSeek timeout, configured provider order, output validation, refusal behavior, token limits and paid fallback opt-in are preserved.

Five private phases fit inside a persisted 2,400-second document deadline, including worst-case 120-second DeepSeek plus 330-second Celar attempts and 150 seconds of overhead. Normal-SA acceptance waits 2,520 seconds. The short-lived fixture authorization is not extended. Phase prompts request concise, substantive work packages while retaining the full detailed contract, effort rationale and unknown-fact handling.

Gateway 1.1.8 adds allowlisted numeric model load, prompt evaluation and token evaluation metrics. No source text, model output or credentials are logged. The existing Oracle GitOps process installs the reviewed runtime package; the existing predeployment runtime-version gate requires it before API rollout. No production deployment or paid-provider opt-in is included.

Synthetic deadline/negative-input tests and application regressions precede the protected rollout. Full success still requires the live normal-SA and fixture acceptance gates; a longer window alone is not model qualification. If this bounded attempt fails, inspect its diagnostics and stop rather than repeatedly generating.
