# Module 064 systemwide failover repair

The supplied Test HAR confirms DeepSeek successfully generated a timesheet in 19.6 seconds. Missing UI mappings displayed Shared AI router, and the API default message incorrectly said the provider declined. Ask Celar returned an Azure Application Gateway 504 after 60 seconds.

## Shared routing

Chat/help, timesheets, enterprise composition, SOW, FlowHive, Project Forge and external reasoning already enter CelarAiCapabilityRouter. Direct private RAG generation now reads the persisted feature route too, selects eligible private targets in that order, and does not silently assume DeepSeek precedes Celar. A router-selected private callback executes only that target. Private-document sources never enter public providers; the outer router owns sanitized generic assistance. Embeddings, OCR and malware scanning retain their modality-specific runtime services.

Interactive router generation has a 40-second inference budget, at most 22 seconds per attempt, reserving six seconds for each later model target. A timed-out attempt is cancelled and recorded as provider_deadline_exceeded so fallback can continue. Caller cancellation and provider refusals remain terminal. Background SOW/FlowHive/Forge jobs retain longer inference budgets. Database, retrieval, tool execution and gateway overhead are outside this inference budget; live acceptance is required before asserting end-to-end completion within 60 seconds.

Disabled, unconfigured, unregistered or circuit-open providers are skipped. Rejected external output remains withheld and recorded as a failed answer, but no longer opens an availability circuit across unrelated requests. No privacy validation is removed.

Both timesheet UI implementations identify deepseek_v4 as DeepSeek v4, and the successful API response uses a success message.

## Verification

DeepSeekProviderTests exercises cancelled attempts, progression to the next provider, caller cancellation, terminal refusal preservation, background budget separation and rejection/circuit isolation. Existing router, consumer, privacy and controller tests remain required. The exact release scope is checked before consumer boundary compatibility is applied. Protected Test only; runtime acceptance pending.
