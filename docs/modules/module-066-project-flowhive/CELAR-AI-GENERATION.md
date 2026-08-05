# Project FlowHive Celar AI production generation

Project FlowHive uses the detailed Celar AI production route:

```http
POST /api/project-flowhive/ai/production-generate
```

The earlier preview and compatibility endpoints remain available for diagnostics and backward compatibility. The production AI Draft Studio uses this dedicated route so there is no duplicate endpoint registration.

The production generation route:

1. resolves the actual and effective Pulse user;
2. confirms Module 011 authorization;
3. sends the selected authorized project to the private Celar AI enterprise composer;
4. retrieves authorized private project evidence through RAG when available;
5. optionally uses only generic sanitized Module 064 reasoning assistance;
6. converts the result to the Module 066 plan contract;
7. validates WBS, dependencies, assignments, durations, and constraints;
8. calculates the deterministic weekday schedule, critical path, float, and planned hours; and
9. returns detailed tasks, risks, assumptions, citations, missing evidence, conflicts, confidence, and review controls.

No generation request persists or baselines the plan, assigns resources, reserves capacity, approves work, creates a customer link, or commits a customer date.
