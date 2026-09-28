# Security remediation tracker — 2026-09-28

This draft PR consolidates the security remediation work and tracks all 149 findings.
It is **not a report-wide security clearance** and must not be presented as one.
The report evaluated mirror commit `8947f0294c75c7012a626b5b0cf49dc8497e36f3`;
this change started from source main `6c70385ddc1085522e310b89b156ee770e154855`
and includes main through `a562371a0bbed74e881c40a4c246928dac9ec72e` (PR #1205).
The source repository is public. Keep the original report, unresolved exploit
narratives, customer information and operational evidence in the restricted
security review channel. Finding numbers below map to that report in order.

## Scope and validation

- 37 findings have a source fix in this PR; 25 have partial remediation;
  87 remain open. No finding is closed by this PR.
- All three Critical findings have source changes and offline exploit regressions.
- Backend build: passed, zero errors; 517 existing-style compiler/analyzer warnings
  remain and are not waived or treated as a clean static-analysis result.
- Frontend source-contract checks and Vite production bundle: passed. The initial
  build lacked Vite; dependencies were installed from the lockfile, then the
  production bundle was built successfully.
- New Python boundary suite: 13 tests, including hostile release fields, dispatch
  inputs, backup batch commands, secret logging and artifact privacy.
- Runtime helper suite: 70 assertions, including IANA/Windows time-zone aliases,
  DST, malicious device paths, private file permissions and symlink replacement.
- Added CSV/navigation, Office archive/XML budget and real HTTP GET/HEAD tests.
- FlowHive executable WBS suite: 539 assertions, including calendar equivalence and
  task limits. Existing authorization suite: 58 checks plus 14 authority/origin checks.
- Isolated PostgreSQL identity/session tests are wired into CI; they have not run
  locally because an isolated database is unavailable.
- Existing installed-release resolver: 35 tests; installed acceptance: 31 tests;
  Teams delivery protocol: 304 assertions. No live cloud or Microsoft calls.
- Repository security posture, system-wide release contract, shell syntax and
  whitespace validation pass locally. GitHub checks must pass on the PR commit.
- Protected Test has not received these changes. Live acceptance is still required
  after review and any separately authorized merge/deployment.

## Consolidation and CI status

The contract/billing changes already merged as PR #1205 are retained through a
normal merge from main. The Trivy reference repair proposed in PR #1207 is included
using the verified v0.28.0 commit, and every Release action now has an immutable
commit reference. This does not close the remaining release-provenance finding.

The initial PR commit passed the repository security job and GitGuardian, but
several historical branch/file-scope checks rejected the combined source set.
Those authorization and deployment checks have not been disabled or marked as
approved. The PR remains a draft pending those checks, complete finding review
and installed verification. Consolidation is not deployment approval.

## Review boundaries

Release assets are parsed as four validated data fields. Registry releases still
require full immutable digests. The existing offline local build path now records
immutable Docker image IDs, and downloaded manifests cannot request local IDs.
Operator-owned `deploy.env` remains trusted shell configuration.

Workflow source/main checks, confirmation values, exact SHA gates, native Test
environment protection and immutable deployment verification are preserved.
Free-text dispatch values cross into Bash through quoted environment variables.

Backup settings reject control characters before writing and are created as 0600
files with atomic replacement. The root backup upload script parses an allowlist
of literal data keys and quotes batch arguments. Existing root-owned PostgreSQL
operator configuration remains a separate trusted input. These changes do not
claim that every VM/root trust boundary in the report has been fixed.

UAT responses remain private to the runner. Publication projects only a fixed
allowlist of status/boolean/hash fields and validated installation receipt fields
into a new 0700 directory containing 0600 JSON files. No raw bodies or screenshots
are copied. A projection failure blocks upload. Retention is reduced to three
days. Installation receipts retain their existing filenames, so the resolver
still enforces GitHub artifact provenance and digest, immutable image agreement,
migration completeness, rollback exclusion and live release identity. The tests
exercise that resolver with the projected receipts and still reject tampering.

## Required operational follow-up

These items cannot be completed by a source-only PR:

- Review historical UAT artifacts, restrict access and remove exposed copies
  through the approved incident process; review who could access them.
- Rotate any credential identified as exposed in historical logs and verify the
  replacement is absent from retained logs. Review log relays for transformed
  secret exposure. Do not copy log bodies or credentials into this public PR.
- Apply corrected ownership/modes to existing backup settings and dump files;
  this PR establishes private permissions for newly written files only.
- Install the updated API and root backup script together in the VM deployment;
  validate a backup upload with controlled credentials and a controlled server.
- Re-run authenticated Protected Test acceptance and inspect the actual published
  artifact against the allowlist before declaring the evidence issue resolved.
- Continue review and remediation of every open finding below. No authorization,
  identity, data-scope, parser, integration or remaining deployment finding is
  implicitly waived by consolidation into this PR.

## Finding ledger

“Source fix” means a source repair is implemented, with local regression coverage
where available; review, complete CI, database and installed verification remain.
Validation above states exactly which suites ran. “Partial” means the source change covers only part of the finding.
“Open” means not resolved or cleared by this PR; it does not assert that the older
scan is still reproducible on main. Details remain in the restricted report.

| Finding | Severity | Status | Evidence or remaining work |
| --- | --- | --- | --- |
| 001 | Critical | Source fix | Release metadata parser; immutable registry digests and local image IDs; no shell evaluation. |
| 002 | Critical | Source fix | Quoted environment inputs in all affected workflow run scripts. |
| 003 | Critical | Source fix | Control-character validation, literal backup configuration loading, quoted SFTP batch arguments. |
| 004 | High | Open | Verify against current main; security review required. |
| 005 | High | Open | Verify against current main; security review required. |
| 006 | High | Partial | Container entrypoint no longer emits credentials; historical exposure and log relay review remain. |
| 007 | High | Partial | Release actions pinned to verified commit SHAs, including PR #1207 Trivy repair; release provenance and permission separation remain. |
| 008 | High | Partial | SSO binding hardened; remaining import paths require separate verification. |
| 009 | High | Open | Verify against current main; security review required. |
| 010 | High | Source fix | Installed time-zone catalog lookup. |
| 011 | High | Source fix | Installed time-zone catalog lookup. |
| 012 | High | Source fix | Installed time-zone catalog lookup. |
| 013 | High | Source fix | Installed time-zone catalog lookup. |
| 014 | High | Open | Verify against current main; security review required. |
| 015 | High | Source fix | Installed time-zone catalog lookup. |
| 016 | High | Source fix | Installed time-zone catalog lookup. |
| 017 | High | Open | Verify against current main; security review required. |
| 018 | High | Open | Verify against current main; security review required. |
| 019 | High | Partial | Maintained deploy and installed-acceptance artifacts use projected evidence; historical artifacts and access review remain. |
| 020 | High | Source fix | Installed time-zone catalog lookup. |
| 021 | High | Source fix | Office archive, XML, cell and rectangle budgets before rich parsing. |
| 022 | High | Source fix | Bounded XML depth and a single traversal of document paragraphs. |
| 023 | High | Source fix | Internal-only navigation validation at response and browser boundaries. |
| 024 | High | Source fix | Office and CSV work budgets before lab import expansion. |
| 025 | High | Source fix | Bounded weekday arithmetic with exhaustive weekday/weekend regression comparisons. |
| 026 | High | Source fix | Authority bound to the validated session user ID; mutable identity fallbacks removed. |
| 027 | High | Open | Verify against current main; security review required. |
| 028 | High | Source fix | Oversized task graphs rejected before traversal and scheduling. |
| 029 | High | Partial | Privileged-write authority narrowed; full persisted-profile verification remains. |
| 030 | High | Partial | Secret writes require explicit authority; full integration review remains. |
| 031 | High | Open | Verify against current main; security review required. |
| 032 | High | Open | Verify against current main; security review required. |
| 033 | High | Open | Verify against current main; security review required. |
| 034 | High | Source fix | Unmatched routes and unknown methods use bounded metric keys. |
| 035 | High | Open | Verify against current main; security review required. |
| 036 | High | Partial | Directory-sync authority narrowed; state-transition review remains. |
| 037 | High | Open | Verify against current main; security review required. |
| 038 | High | Open | Verify against current main; security review required. |
| 039 | High | Source fix | Existing project upload authorization runs before expense extraction. |
| 040 | High | Open | Verify against current main; security review required. |
| 041 | High | Open | Verify against current main; security review required. |
| 042 | High | Open | Verify against current main; security review required. |
| 043 | High | Open | Verify against current main; security review required. |
| 044 | High | Open | Verify against current main; security review required. |
| 045 | High | Partial | SSO lookup and linking hardened; import-path review remains. |
| 046 | High | Open | Verify against current main; security review required. |
| 047 | High | Open | Verify against current main; security review required. |
| 048 | High | Open | Verify against current main; security review required. |
| 049 | High | Open | Verify against current main; security review required. |
| 050 | High | Partial | Shared API path canonicalization added; full route integration verification remains. |
| 051 | High | Open | Verify against current main; security review required. |
| 052 | High | Source fix | Office intake preflight bounds archive expansion, XML and spreadsheet ranges. |
| 053 | High | Open | Verify against current main; security review required. |
| 054 | High | Partial | HTML regex timeouts added; complete parser timing verification remains. |
| 055 | High | Partial | Shared API path canonicalization added; full import-route verification remains. |
| 056 | High | Source fix | Atomic 0600 writes for newly saved backup settings. |
| 057 | High | Source fix | Microsoft environment and public origin resolved from deployment configuration. |
| 058 | High | Open | Verify against current main; security review required. |
| 059 | High | Source fix | Ordinary Administrator no longer aliases permanent Super Administrator authority. |
| 060 | High | Source fix | Office archive and XML limits applied before routing-directory extraction. |
| 061 | High | Partial | Completion target protection added; full password lifecycle review remains. |
| 062 | High | Partial | User mutation target protection expanded; database and route verification remain. |
| 063 | High | Partial | Password target protection expanded; database and route verification remain. |
| 064 | High | Open | Verify against current main; security review required. |
| 065 | High | Partial | User mutation target protection expanded; database and route verification remain. |
| 066 | High | Partial | SSO runtime writes narrowed; full identity-provider lifecycle review remains. |
| 067 | High | Open | Verify against current main; security review required. |
| 068 | High | Open | Verify against current main; security review required. |
| 069 | Medium | Open | Verify against current main; security review required. |
| 070 | Medium | Source fix | Private deployment backup directory and restrictive umask for new dumps. |
| 071 | Medium | Open | Verify against current main; security review required. |
| 072 | Medium | Open | Verify against current main; security review required. |
| 073 | Medium | Open | Verify against current main; security review required. |
| 074 | Medium | Source fix | HEAD uses the same confined static-file/proxy path as GET; real HTTP regression. |
| 075 | Medium | Open | Verify against current main; security review required. |
| 076 | Medium | Open | Verify against current main; security review required. |
| 077 | Medium | Open | Verify against current main; security review required. |
| 078 | Medium | Open | Verify against current main; security review required. |
| 079 | Medium | Open | Verify against current main; security review required. |
| 080 | Medium | Open | Verify against current main; security review required. |
| 081 | Medium | Open | Verify against current main; security review required. |
| 082 | Medium | Open | Verify against current main; security review required. |
| 083 | Medium | Open | Verify against current main; security review required. |
| 084 | Medium | Open | Verify against current main; security review required. |
| 085 | Medium | Open | Verify against current main; security review required. |
| 086 | Medium | Open | Verify against current main; security review required. |
| 087 | Medium | Open | Verify against current main; security review required. |
| 088 | Medium | Open | Verify against current main; security review required. |
| 089 | Medium | Open | Verify against current main; security review required. |
| 090 | Medium | Open | Verify against current main; security review required. |
| 091 | Medium | Open | Verify against current main; security review required. |
| 092 | Medium | Open | Verify against current main; security review required. |
| 093 | Medium | Open | Verify against current main; security review required. |
| 094 | Medium | Open | Verify against current main; security review required. |
| 095 | Medium | Open | Verify against current main; security review required. |
| 096 | Medium | Open | Verify against current main; security review required. |
| 097 | Medium | Partial | Mail runtime writes narrowed; complete transport and credential review remains. |
| 098 | Medium | Open | Verify against current main; security review required. |
| 099 | Medium | Open | Verify against current main; security review required. |
| 100 | Medium | Open | Verify against current main; security review required. |
| 101 | Medium | Source fix | Handler rejects retired approval actions after model binding. |
| 102 | Medium | Open | Verify against current main; security review required. |
| 103 | Medium | Source fix | Encoded photo input bounded before scanning, slicing and base64 allocation. |
| 104 | Medium | Open | Verify against current main; security review required. |
| 105 | Medium | Open | Verify against current main; security review required. |
| 106 | Medium | Open | Verify against current main; security review required. |
| 107 | Medium | Open | Verify against current main; security review required. |
| 108 | Medium | Open | Verify against current main; security review required. |
| 109 | Medium | Open | Verify against current main; security review required. |
| 110 | Medium | Open | Verify against current main; security review required. |
| 111 | Medium | Open | Verify against current main; security review required. |
| 112 | Medium | Open | Verify against current main; security review required. |
| 113 | Medium | Open | Verify against current main; security review required. |
| 114 | Medium | Partial | Shared API path canonicalization added; full route verification remains. |
| 115 | Medium | Open | Verify against current main; security review required. |
| 116 | Medium | Partial | Shared API path canonicalization added; full route verification remains. |
| 117 | Medium | Open | Verify against current main; security review required. |
| 118 | Medium | Open | Verify against current main; security review required. |
| 119 | Medium | Open | Verify against current main; security review required. |
| 120 | Medium | Open | Verify against current main; security review required. |
| 121 | Low | Open | Verify against current main; security review required. |
| 122 | Low | Source fix | Authenticated failure screenshot capture removed; maintained uploads projected. |
| 123 | Low | Open | Verify against current main; security review required. |
| 124 | Low | Source fix | Currency validation on intake and defensive UI formatting. |
| 125 | Low | Source fix | Shared formula-safe CSV serialization. |
| 126 | Low | Source fix | Shared formula-safe CSV serialization. |
| 127 | Low | Open | Verify against current main; security review required. |
| 128 | Low | Open | Verify against current main; security review required. |
| 129 | Low | Open | Verify against current main; security review required. |
| 130 | Low | Partial | Shared administrator checks narrowed; full operations review remains. |
| 131 | Low | Open | Verify against current main; security review required. |
| 132 | Low | Open | Verify against current main; security review required. |
| 133 | Low | Open | Verify against current main; security review required. |
| 134 | Low | Source fix | Shared formula-safe CSV serialization. |
| 135 | Low | Partial | CI/CD operations require administrator roles; remaining release controls require review. |
| 136 | Low | Open | Verify against current main; security review required. |
| 137 | Low | Source fix | Shared formula-safe CSV serialization. |
| 138 | Low | Open | Verify against current main; security review required. |
| 139 | Low | Source fix | Shared formula-safe CSV serialization. |
| 140 | Low | Open | Verify against current main; security review required. |
| 141 | Low | Partial | Shared API path canonicalization added; full browser callback verification remains. |
| 142 | Low | Open | Verify against current main; security review required. |
| 143 | Low | Source fix | Storage components reject dot-only names; destination constrained to its root. |
| 144 | Low | Open | Verify against current main; security review required. |
| 145 | Low | Partial | Shared API path canonicalization added; full endpoint verification remains. |
| 146 | Low | Source fix | Shared formula-safe CSV serialization. |
| 147 | Low | Partial | Session lifetime and password-change checks implemented; database regressions pending CI. |
| 148 | Low | Open | Verify against current main; security review required. |
| 149 | Low | Partial | Time-export download blocked in View-As; full stateful-read review remains. |
