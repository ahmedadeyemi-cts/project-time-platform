# Celar AI production platform source manifest

Branch: `feature/celar-ai-production-platform-20260802`

Baseline: `main@f083c00de3e4d98cbb611952acd90e0721b08669`

## Backend production runtime

- `src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs`
  - intent-first chat orchestration;
  - deterministic date/time, version, and capability answers;
  - answer trust and evidence gate;
  - lifecycle schema initialization;
  - dataset, training, evaluation, model, deployment, and answer-quality persistence;
  - allowlisted private training submission;
  - detailed Project FlowHive generation and deterministic scheduling.
- `src/backend/ProjectTime.Api/Modules/CelarAiEnterprisePlatformModule.cs`
  - registers the production endpoint family from the existing Celar AI application boundary;
  - extends the architecture response with intent, trust, and model-lifecycle layers.

## Module 011 and Project FlowHive experience

- `src/frontend/project-time-web/src/CelarAiProductionPlatform.jsx`
- `src/frontend/project-time-web/src/celar-ai-production-platform.css`
- `src/frontend/project-time-web/scripts/inject-celar-ai-production-platform.mjs`
- `src/frontend/project-time-web/scripts/inject-celar-ai-enterprise-chat-context.mjs`

## Validation and documentation

- `.github/workflows/celar-ai-production-platform-ci.yml`
- `src/frontend/project-time-web/scripts/validate-celar-ai-production-platform.mjs`
- `docs/modules/module-011-pulse-ai/CELAR-AI-PRODUCTION-PLATFORM.md`
- `docs/modules/module-011-pulse-ai/PRODUCTION-ACCEPTANCE-CRITERIA.md`
- `docs/modules/module-066-project-flowhive/CELAR-AI-GENERATION.md`

## Runtime schema

The package does not alter an existing numbered migration or protected deployment control. An actual-session administrator explicitly initializes the idempotent, audited lifecycle metadata schema from Module 011 Governance.

Schema identifier:

```text
celar_ai_production_platform_runtime_v1
```

The runtime schema stores dataset/model artifact references, checksums, job/evaluation/model/deployment metadata, answer-quality evidence, and lifecycle audit events. It does not store raw training examples or model binaries.

## Locked outcomes

The source does not deploy itself, call a public provider with private context, submit Timesheets, publish SOWs, baseline FlowHive plans, assign resources, reserve capacity, commit customer dates, change financial records, grant permissions, or promote a model without separate human-controlled operations.
