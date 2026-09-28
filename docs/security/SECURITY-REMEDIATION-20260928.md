# Security remediation tracker — 2026-09-28

This draft PR is the first remediation set for the security team's 149-finding report.
It is **not a report-wide security clearance** and must not be presented as one.
The report evaluated mirror commit `8947f0294c75c7012a626b5b0cf49dc8497e36f3`;
this change starts from source main `6c70385ddc1085522e310b89b156ee770e154855`.
The source repository is public. Keep the original report, unresolved exploit
narratives, customer information and operational evidence in the restricted
security review channel. Finding numbers below map to that report in order.

## Scope and validation

- 13 findings have a source fix in this PR; 2 have partial remediation; 134 remain
  open for verification against current main. No finding is closed by this PR.
- All three Critical findings have source changes and offline exploit regressions.
- Backend build: passed, zero errors; 517 existing-style compiler/analyzer warnings
  remain and are not waived or treated as a clean static-analysis result.
- New Python boundary suite: 12 tests, including hostile release fields, dispatch
  inputs, backup batch commands, secret logging and artifact privacy.
- Runtime helper suite: 28 assertions, including IANA/Windows time-zone aliases,
  DST, malicious device paths, private file permissions and symlink replacement.
- Existing installed-release resolver: 35 tests; installed acceptance: 31 tests;
  Teams delivery protocol: 304 assertions. No live cloud or Microsoft calls.
- Repository security posture, system-wide release contract, shell syntax and
  whitespace validation pass locally. GitHub checks must pass on the PR commit.
- Protected Test has not received these changes. Live acceptance is still required
  after review and any separately authorized merge/deployment.

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
  implicitly waived by this first set of fixes.

## Finding ledger

“Source fix” means implemented and locally tested, pending review, CI and installed
verification. “Partial” means the source change covers only part of the finding.
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
| 007 | High | Open | Verify against current main; security review required. |
| 008 | High | Open | Verify against current main; security review required. |
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
| 021 | High | Open | Verify against current main; security review required. |
| 022 | High | Open | Verify against current main; security review required. |
| 023 | High | Open | Verify against current main; security review required. |
| 024 | High | Open | Verify against current main; security review required. |
| 025 | High | Open | Verify against current main; security review required. |
| 026 | High | Open | Verify against current main; security review required. |
| 027 | High | Open | Verify against current main; security review required. |
| 028 | High | Open | Verify against current main; security review required. |
| 029 | High | Open | Verify against current main; security review required. |
| 030 | High | Open | Verify against current main; security review required. |
| 031 | High | Open | Verify against current main; security review required. |
| 032 | High | Open | Verify against current main; security review required. |
| 033 | High | Open | Verify against current main; security review required. |
| 034 | High | Open | Verify against current main; security review required. |
| 035 | High | Open | Verify against current main; security review required. |
| 036 | High | Open | Verify against current main; security review required. |
| 037 | High | Open | Verify against current main; security review required. |
| 038 | High | Open | Verify against current main; security review required. |
| 039 | High | Open | Verify against current main; security review required. |
| 040 | High | Open | Verify against current main; security review required. |
| 041 | High | Open | Verify against current main; security review required. |
| 042 | High | Open | Verify against current main; security review required. |
| 043 | High | Open | Verify against current main; security review required. |
| 044 | High | Open | Verify against current main; security review required. |
| 045 | High | Open | Verify against current main; security review required. |
| 046 | High | Open | Verify against current main; security review required. |
| 047 | High | Open | Verify against current main; security review required. |
| 048 | High | Open | Verify against current main; security review required. |
| 049 | High | Open | Verify against current main; security review required. |
| 050 | High | Open | Verify against current main; security review required. |
| 051 | High | Open | Verify against current main; security review required. |
| 052 | High | Open | Verify against current main; security review required. |
| 053 | High | Open | Verify against current main; security review required. |
| 054 | High | Open | Verify against current main; security review required. |
| 055 | High | Open | Verify against current main; security review required. |
| 056 | High | Source fix | Atomic 0600 writes for newly saved backup settings. |
| 057 | High | Open | Verify against current main; security review required. |
| 058 | High | Open | Verify against current main; security review required. |
| 059 | High | Open | Verify against current main; security review required. |
| 060 | High | Open | Verify against current main; security review required. |
| 061 | High | Open | Verify against current main; security review required. |
| 062 | High | Open | Verify against current main; security review required. |
| 063 | High | Open | Verify against current main; security review required. |
| 064 | High | Open | Verify against current main; security review required. |
| 065 | High | Open | Verify against current main; security review required. |
| 066 | High | Open | Verify against current main; security review required. |
| 067 | High | Open | Verify against current main; security review required. |
| 068 | High | Open | Verify against current main; security review required. |
| 069 | Medium | Open | Verify against current main; security review required. |
| 070 | Medium | Source fix | Private deployment backup directory and restrictive umask for new dumps. |
| 071 | Medium | Open | Verify against current main; security review required. |
| 072 | Medium | Open | Verify against current main; security review required. |
| 073 | Medium | Open | Verify against current main; security review required. |
| 074 | Medium | Open | Verify against current main; security review required. |
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
| 097 | Medium | Open | Verify against current main; security review required. |
| 098 | Medium | Open | Verify against current main; security review required. |
| 099 | Medium | Open | Verify against current main; security review required. |
| 100 | Medium | Open | Verify against current main; security review required. |
| 101 | Medium | Open | Verify against current main; security review required. |
| 102 | Medium | Open | Verify against current main; security review required. |
| 103 | Medium | Open | Verify against current main; security review required. |
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
| 114 | Medium | Open | Verify against current main; security review required. |
| 115 | Medium | Open | Verify against current main; security review required. |
| 116 | Medium | Open | Verify against current main; security review required. |
| 117 | Medium | Open | Verify against current main; security review required. |
| 118 | Medium | Open | Verify against current main; security review required. |
| 119 | Medium | Open | Verify against current main; security review required. |
| 120 | Medium | Open | Verify against current main; security review required. |
| 121 | Low | Open | Verify against current main; security review required. |
| 122 | Low | Source fix | Authenticated failure screenshot capture removed; maintained uploads projected. |
| 123 | Low | Open | Verify against current main; security review required. |
| 124 | Low | Open | Verify against current main; security review required. |
| 125 | Low | Open | Verify against current main; security review required. |
| 126 | Low | Open | Verify against current main; security review required. |
| 127 | Low | Open | Verify against current main; security review required. |
| 128 | Low | Open | Verify against current main; security review required. |
| 129 | Low | Open | Verify against current main; security review required. |
| 130 | Low | Open | Verify against current main; security review required. |
| 131 | Low | Open | Verify against current main; security review required. |
| 132 | Low | Open | Verify against current main; security review required. |
| 133 | Low | Open | Verify against current main; security review required. |
| 134 | Low | Open | Verify against current main; security review required. |
| 135 | Low | Open | Verify against current main; security review required. |
| 136 | Low | Open | Verify against current main; security review required. |
| 137 | Low | Open | Verify against current main; security review required. |
| 138 | Low | Open | Verify against current main; security review required. |
| 139 | Low | Open | Verify against current main; security review required. |
| 140 | Low | Open | Verify against current main; security review required. |
| 141 | Low | Open | Verify against current main; security review required. |
| 142 | Low | Open | Verify against current main; security review required. |
| 143 | Low | Open | Verify against current main; security review required. |
| 144 | Low | Open | Verify against current main; security review required. |
| 145 | Low | Open | Verify against current main; security review required. |
| 146 | Low | Open | Verify against current main; security review required. |
| 147 | Low | Open | Verify against current main; security review required. |
| 148 | Low | Open | Verify against current main; security review required. |
| 149 | Low | Open | Verify against current main; security review required. |
