# Module 066D — Shared AI Integration

## Required dependency

Module 066 is a consumer of Module 064. It is not an AI provider configuration
module and contains no direct provider client.

The release train includes the Module 064 shared router. If FlowHive provider
execution is separately authorized, its reviewed activation adapter must construct:

- feature: `ProjectPulseAiFeatures.ProjectFlowHivePlan`;
- system prompt: draft-only, citation and conflict requirements;
- user prompt: sanitized project metadata and approved GSD/SOW excerpts;
- maximum output: 2,600 tokens by default;
- temperature: 0.1;
- local fallback: the deterministic supplied-task template.

It must call `ProjectPulseAiRouter.GenerateAsync`. Module 064 owns health checks,
circuit guards, rate limits, model allowlists, sanitized errors, and route order.

## Provider policy

1. Claude first when configured, healthy, and permitted.
2. OpenAI only when Claude is unavailable/disabled/unconfigured.
3. Governed local template last.
4. A provider safety refusal terminates routing with no fallback.

## Source authority

The response must identify GSD/SOW document versions, cite source sections,
surface conflicts, label assumptions, and leave missing commitments unresolved.
AI output cannot modify canonical tasks, store a plan, establish a baseline, or
create a customer artifact without separate human-reviewed actions.
