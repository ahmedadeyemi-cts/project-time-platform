# Governed provider answer repair (PR #1219)

Baseline: `98f80675b50e72c450f96f278c87168939a0ad2e` (merged PR #1218).

## Demonstrated defect

Protected Test run `36655167954` reported successful Gemini generation after
DeepSeek/OpenAI circuit-open skips and Celar/Claude deadlines. The application
only promoted Claude/OpenAI external answers. The reliability service also
omitted Gemini/Copilot from stable-public answer preservation and the explicit
external-model/internal-fact boundary.

A compiled regression on unchanged baseline source failed at:
`gemini: stable model content is not replaced by a placeholder`.
With the repair the 120-case reliability corpus, 81 production answer-selection
assertions, and 61 six-model reliability assertions pass. Provider identity is
preserved. Model memory remains partial and confidence-capped, not verified.
Refusals, conflicts, current-fact verification, empty results and unsupported
enterprise claims retain their existing fail-closed behavior.

## Release boundary and remaining blocker

This PR does not repair upstream provider availability or increase deadlines.
The frozen Test workflow currently accepts model identities `deepseek_v4`,
`celar_ai`, `claude`, and `openai` only. Gemini/Copilot remain excluded by that
unchanged acceptance contract. Do not relabel either provider or claim that
this application repair alone satisfies protected acceptance.

Deployment/Production controllers, candidate and approval files, installed-UAT
assertions, routing order, credentials, circuits, Oracle configuration and
private-document evidence gates remain unchanged. The existing exact source
registration binds PR/repository/branch/base, complete file inventory and source
hashes. It authorizes no deployment and does not waive native protections.

No security finding is newly closed by this source regression. The private
planner and remaining installed/browser acceptance paths still need live proof.
