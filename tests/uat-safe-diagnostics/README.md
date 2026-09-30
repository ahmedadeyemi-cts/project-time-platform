# Content-free failure diagnostics

Baseline: `ab9f39d3d1ccf33f7bffec779300a8bc900db629`.

Protected deployment 36780377208 installed healthy API/web images but failed its
functional acceptance step. Its independently verified six-file public artifact
contained installation evidence only: no failed assertion and no UAT response
predicate evidence. Detailed historical job-log access was not available. A
synthetic fallback response reproduced the publisher discarding all relevant
failure information.

This patch changes the evidence publisher, not the application, its accounts,
provider selection, deadline, acceptance criteria or protected release authority.
The existing summary gains an optional `functionalUatDiagnostics` object based
on a finite list of files already produced by the same acceptance step. It shows
which artifacts were recorded, fixed content/source/routing predicates, known
provider outcomes/reason codes and finite HTTP-status categories. It never emits
raw answers, prompts, customer names, account identities, URLs, credentials,
exception text, arbitrary reason strings, screenshots or raw log content.

Artifact presence is not a pass. The new output cannot claim acceptance, infer
wall-clock timing, or label a missing file as a failed check. The two Module064
JSON predicate projections are checked against the actual unchanged jq gates on
synthetic positive and negative responses. Unrecognized response shapes and
missing timing remain unknown. The original installation receipts, their field
validation, their pre-projection hashes and functional-acceptance resolver remain
unchanged.

File inputs use directory-relative no-follow opens, reject non-regular objects,
and enforce byte, JSON-depth and record-count limits. Invalid input yields a
fixed diagnostic category; symlinks or unsafe filesystem objects stop publication
without creating the output directory. Tests use synthetic content only.

A future diagnostic retest can now provide actionable evidence while retaining
all protected checks. This does not explain the old failure retrospectively,
activate ClamAV/Tesseract/Laya containers, approve their inactive deployment
proposal, or close any of the original 149 security findings. A controlled retest
must be explicitly reported as diagnostic until full acceptance is proven.
