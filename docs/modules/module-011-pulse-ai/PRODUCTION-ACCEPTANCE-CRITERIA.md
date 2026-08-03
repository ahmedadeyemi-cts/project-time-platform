# Celar AI production acceptance criteria

The pull request is not merge-ready until all of the following are true:

- The exact PR head builds the .NET 10 API with zero errors.
- The complete frontend production build passes all existing validators.
- The production source injector is idempotent on consecutive executions.
- The production web container builds successfully.
- Module 011 renders one authoritative shell with populated Overview, Knowledge & RAG, Tools & Coverage, Datasets, Training, Evaluations, Model Registry, Deployments, and Governance workspaces.
- The Overview displays the US Signal Celar AI architecture diagram and `Created by Dr. Ahmed Adeyemi` attribution.
- `What day is it today?`, `What is the current system version?`, `What can Celar AI answer?`, and common `How do I...` questions return direct answers without irrelevant API counts.
- Every chat answer displays a trust classification, confidence, successful/failed source counts, evidence reasons, and human-review requirement.
- Project FlowHive calls `/api/project-flowhive/ai/production-generate`, produces a detailed review plan, and runs deterministic schedule validation.
- FlowHive generation never persists, baselines, assigns, reserves, approves, publishes, or commits dates.
- Private document content never reaches Claude or OpenAI.
- Fine-tuning orchestration transmits only an approved immutable artifact URI, checksum, method, base model, and governed configuration to an allowlisted private endpoint.
- View-As cannot initialize schema or perform lifecycle writes.
- Repository security and source-boundary checks pass.
