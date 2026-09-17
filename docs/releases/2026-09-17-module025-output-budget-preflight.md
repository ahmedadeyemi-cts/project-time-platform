# Module 025 cloud output budget and qualification preflight

## Recorded failure

Protected Test qualification [35229710153](https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/35229710153), completed September 17, 2026, used merged commit `5f2f520890309db20d3897f56c09c1ae194a4e33` and the OpenAI model selected in Module 064. Its authenticated evidence records `incomplete / max_output_tokens`: 6,144 output tokens, including 438 reasoning tokens, after 57,398 ms. The partial Plan was rejected before contract validation. This was not a provider timeout. No application deployment or full lifecycle acceptance occurred.

The same qualification's post-check found unchanged revision identity but template differences in `container_other` and `template_other`. It did not retain exact differing fields. The cause of those differences is unproven; this change does not waive them.

## Correction

- Cloud structured SOW requests use a bounded 12,288-token ceiling in both OpenAI and Claude transports. The private provider remains at 6,144. Non-SOW requests retain their existing configuration.
- Each detailed phase remains bounded at 96,000 characters. The assembled five-phase document allows 512,000 characters, accommodating all five phase bounds and metadata. The parser and generation engine enforce the corresponding limits. Every existing detail field remains required; incomplete output remains unusable.
- Prompt instructions state the actual provider ceiling and discourage repeated prose without removing the detailed contract. The persisted 20-minute deadline, 120-second provider deadline, two attempts per phase, durable checkpoints, and source identity checks remain unchanged. The cloud ceiling is not a guarantee of completion or a token reservation. At most ten phase attempts are permitted (up to 122,880 output tokens before deadline/cancellation limits).
- Qualification reports its actual token, phase, document, and provider-time bounds.
- Every API snapshot uses a read-only ARM GET with the same pinned `2024-03-01` API version. Stable revision and template checks run before image build and again immediately before inference. The strict post-check remains. A failing early comparison prevents the provider call; an owned temporary job is still cleaned up if already created.
- Template differences include only allowlisted schema paths and JSON types. Unknown property names are redacted. Environment values, names, secrets, references, and template payloads are not emitted. Missing and null remain distinct and block qualification; only the established semantically equivalent environment ordering is normalized.

## Verification and release boundary

Captured HTTP tests check both cloud ceilings, over-budget request clamping, private and non-SOW isolation, and rejection of the recorded incomplete-response shape. A checkpoint fixture assembles more than 96,000 characters and compares every retained work package and detailed step, without inference. Oversized phases and assembled documents remain rejected.

Qualification tests cover stable same-version snapshots; revision drift, template drift, and read failures at both preflights; zero provider starts on early failure; final drift rejection; safe diagnostics; and temporary-job cleanup. Database recovery and browser lifecycle checks run in existing CI. Exact-file scope checks preserve deployment workflows, native Test approval, admission manifests, Production, SQL, and private runtime configuration.

Live qualification and full installed SOW/GSD plus My Role acceptance remain required. A larger ceiling does not prove the model can satisfy the contract within the deadline. Successful qualification alone does not prove deployment, immutable repeated and historical downloads, or SELL handoff. The existing SELL upload adapter blocker is unchanged.
