# Security remediation continuation — 2026-09-29

Draft source candidate from `e01f559e0bb7df91e233fd885c837f666802cb98`. All 149 findings remain tracked in the restricted review. **No finding is declared closed, and this candidate is not deployed.** The earlier ledger remains a historical record of PR1209, not a current security clearance.

This candidate tightens authorization, document/financial scope, identity refresh, integration secret handling, time immutability and data integrity. It adds isolated migration, authorization and OCR regression coverage. Migration130 execution and independent-key provisioning are mandatory prerequisites; do not merge/deploy this draft before the credential transition, remaining source review and protected release admission are complete.

The active protected deployment workflow and its exact registration remain unchanged. A narrow migration-builder addition packages and verifies migration130 while preserving the private-network runner and all existing release steps. The last inspected UAT run installed its release but failed functional acceptance and evidence publication. No passing installed acceptance is claimed. Historical operational review and remaining source remediations must be completed privately. Do not upload customer data, raw evidence, credential values or the security report to this public repository.

## Validation

- Backend authorization suite: 14 authority, 15 database, 48 completion and 58 authorization checks pass on an isolated PostgreSQL-compatible engine. Real PostgreSQL race behavior remains unverified.
- Migration regression: 21 assertions; OCR: four adversarial tests; deployment boundaries:13 tests; output-security and repository posture pass.
- Frontend source contracts and production build pass. Protected controller tests:5; installed-release resolver:35; installed-acceptance verifier:31; HTTP proxy:1, all pass locally. These are offline verifiers, not a live UAT run.
- Native CI identified stale test fixtures and initialization catalog drift. Follow-up validation passes:305 Teams protocol assertions;19 document-preparation checks;89 sequential-planning checks;142 actual Module019 SQL access assertions;10 production-foundation checks. Malformed keys and missing AI consent are negative cases; runtime rules were not relaxed.
- Module019 overview counts, lists and downloads now enforce document visibility consistently, including restricted bridged records.
- Native feature/controller scope gates rejected this combined branch. Required native CI, reviewed release scope and live authenticated UAT acceptance remain mandatory.

## Continued validation and CI registration

- Retried only the prior main build failure caused by Docker Hub HTTP502; that native workflow now passes.
- Added an exact PR1210 file/content registration with negative tests for wrong identities, extra/missing files, modified bytes, symlinks and deployment-control changes. This registration grants no deployment authority. Existing protected controller bytes and approval paths remain frozen.
- Fixed the FlowHive database fixture adding an already-existing consent column; the production consent requirement is unchanged.
- Removed implicit lab mutation authority from organization-wide scope; explicit module grants and administrator authority are required.
- The existing contract approval/funding database suite passes all 41 cases, including wrong-customer and ineligible funding rejection.
- Further native CI and live acceptance are still required. The deployment access device was offline at the continuation checkpoint; no deployment was attempted.

## Latest authorization verification

- Microsoft services profile activation and SSO profile/secret writes now require permanent Super Administrator authority; delegated mail/system permissions cannot substitute for it.
- All five production-resilience routes require administrator-role admission. The 83-check frontend/source contract now asserts the stronger boundary.
- Actual authorization resolvers and contract handlers pass 16 database-backed cases. Coverage includes delegated privilege rejection, authorized reads, View-As, revoked administrator assignments, contract-balance reads and contract usage writes.
- Full local run:14 authority,16 privileged integration,15 identity/session,64 completion,45 route and58 existing authorization checks passed against an isolated PostgreSQL-compatible engine. Native PostgreSQL validation remains required for this latest change, especially concurrent behavior.
- The prior pushed checkpoint passed68 of72 returned workflows at inspection; one was running and three historical registration failures are addressed in this follow-up. All121 admission tests passed locally without removing assertions. No live deployment occurred.
- Current dispositions:37 prior source repairs,109 candidate repairs with incomplete validation,3 further remediation/review. Zero findings are closed through UAT.

## All-finding disposition

Numbering maps to the restricted report. Source notes and exploit details remain private. Every row requires deployed validation.

| Finding | Source disposition | UAT closure |
| --- | --- | --- |
| 001 | Prior source fix; UAT unverified | Not established |
| 002 | Prior source fix; UAT unverified | Not established |
| 003 | Prior source fix; UAT unverified | Not established |
| 004 | Candidate repair; incomplete validation | Not established |
| 005 | Candidate repair; incomplete validation | Not established |
| 006 | Further remediation/review required | Not established |
| 007 | Candidate repair; incomplete validation | Not established |
| 008 | Candidate repair; incomplete validation | Not established |
| 009 | Candidate repair; incomplete validation | Not established |
| 010 | Prior source fix; UAT unverified | Not established |
| 011 | Prior source fix; UAT unverified | Not established |
| 012 | Prior source fix; UAT unverified | Not established |
| 013 | Prior source fix; UAT unverified | Not established |
| 014 | Candidate repair; live gateway verified, source review pending | Not established |
| 015 | Prior source fix; UAT unverified | Not established |
| 016 | Prior source fix; UAT unverified | Not established |
| 017 | Candidate repair; incomplete validation | Not established |
| 018 | Candidate repair; incomplete validation | Not established |
| 019 | Candidate repair; incomplete validation | Not established |
| 020 | Prior source fix; UAT unverified | Not established |
| 021 | Prior source fix; UAT unverified | Not established |
| 022 | Prior source fix; UAT unverified | Not established |
| 023 | Prior source fix; UAT unverified | Not established |
| 024 | Prior source fix; UAT unverified | Not established |
| 025 | Prior source fix; UAT unverified | Not established |
| 026 | Prior source fix; UAT unverified | Not established |
| 027 | Candidate repair; incomplete validation | Not established |
| 028 | Prior source fix; UAT unverified | Not established |
| 029 | Candidate repair; incomplete validation | Not established |
| 030 | Candidate repair; incomplete validation | Not established |
| 031 | Candidate repair; incomplete validation | Not established |
| 032 | Candidate repair; incomplete validation | Not established |
| 033 | Candidate repair; incomplete validation | Not established |
| 034 | Prior source fix; UAT unverified | Not established |
| 035 | Candidate repair; incomplete validation | Not established |
| 036 | Candidate repair; incomplete validation | Not established |
| 037 | Candidate repair; incomplete validation | Not established |
| 038 | Candidate repair; incomplete validation | Not established |
| 039 | Prior source fix; UAT unverified | Not established |
| 040 | Candidate repair; incomplete validation | Not established |
| 041 | Candidate repair; incomplete validation | Not established |
| 042 | Candidate repair; incomplete validation | Not established |
| 043 | Candidate repair; incomplete validation | Not established |
| 044 | Candidate repair; incomplete validation | Not established |
| 045 | Candidate repair; incomplete validation | Not established |
| 046 | Candidate repair; incomplete validation | Not established |
| 047 | Candidate repair; incomplete validation | Not established |
| 048 | Candidate repair; incomplete validation | Not established |
| 049 | Candidate repair; incomplete validation | Not established |
| 050 | Candidate repair; incomplete validation | Not established |
| 051 | Candidate repair; incomplete validation | Not established |
| 052 | Prior source fix; UAT unverified | Not established |
| 053 | Candidate repair; incomplete validation | Not established |
| 054 | Candidate repair; incomplete validation | Not established |
| 055 | Candidate repair; incomplete validation | Not established |
| 056 | Prior source fix; UAT unverified | Not established |
| 057 | Prior source fix; UAT unverified | Not established |
| 058 | Candidate repair; incomplete validation | Not established |
| 059 | Prior source fix; UAT unverified | Not established |
| 060 | Prior source fix; UAT unverified | Not established |
| 061 | Candidate repair; incomplete validation | Not established |
| 062 | Candidate repair; incomplete validation | Not established |
| 063 | Candidate repair; incomplete validation | Not established |
| 064 | Candidate repair; incomplete validation | Not established |
| 065 | Candidate repair; incomplete validation | Not established |
| 066 | Candidate repair; incomplete validation | Not established |
| 067 | Candidate repair; incomplete validation | Not established |
| 068 | Candidate repair; incomplete validation | Not established |
| 069 | Further remediation/review required | Not established |
| 070 | Prior source fix; UAT unverified | Not established |
| 071 | Candidate repair; incomplete validation | Not established |
| 072 | Candidate repair; incomplete validation | Not established |
| 073 | Further remediation/review required | Not established |
| 074 | Prior source fix; UAT unverified | Not established |
| 075 | Candidate repair; incomplete validation | Not established |
| 076 | Candidate repair; incomplete validation | Not established |
| 077 | Candidate repair; incomplete validation | Not established |
| 078 | Candidate repair; incomplete validation | Not established |
| 079 | Candidate repair; incomplete validation | Not established |
| 080 | Candidate repair; incomplete validation | Not established |
| 081 | Candidate repair; incomplete validation | Not established |
| 082 | Candidate repair; incomplete validation | Not established |
| 083 | Candidate repair; incomplete validation | Not established |
| 084 | Candidate repair; incomplete validation | Not established |
| 085 | Candidate repair; incomplete validation | Not established |
| 086 | Candidate repair; incomplete validation | Not established |
| 087 | Candidate repair; incomplete validation | Not established |
| 088 | Candidate repair; incomplete validation | Not established |
| 089 | Candidate repair; incomplete validation | Not established |
| 090 | Candidate repair; incomplete validation | Not established |
| 091 | Candidate repair; incomplete validation | Not established |
| 092 | Candidate repair; incomplete validation | Not established |
| 093 | Candidate repair; incomplete validation | Not established |
| 094 | Candidate repair; incomplete validation | Not established |
| 095 | Candidate repair; incomplete validation | Not established |
| 096 | Candidate repair; incomplete validation | Not established |
| 097 | Candidate repair; incomplete validation | Not established |
| 098 | Candidate repair; incomplete validation | Not established |
| 099 | Candidate repair; incomplete validation | Not established |
| 100 | Candidate repair; incomplete validation | Not established |
| 101 | Prior source fix; UAT unverified | Not established |
| 102 | Candidate repair; incomplete validation | Not established |
| 103 | Prior source fix; UAT unverified | Not established |
| 104 | Candidate repair; incomplete validation | Not established |
| 105 | Candidate repair; incomplete validation | Not established |
| 106 | Candidate repair; incomplete validation | Not established |
| 107 | Candidate repair; incomplete validation | Not established |
| 108 | Candidate repair; incomplete validation | Not established |
| 109 | Candidate repair; incomplete validation | Not established |
| 110 | Candidate repair; incomplete validation | Not established |
| 111 | Candidate repair; incomplete validation | Not established |
| 112 | Candidate repair; incomplete validation | Not established |
| 113 | Candidate repair; incomplete validation | Not established |
| 114 | Candidate repair; incomplete validation | Not established |
| 115 | Candidate repair; incomplete validation | Not established |
| 116 | Candidate repair; incomplete validation | Not established |
| 117 | Candidate repair; incomplete validation | Not established |
| 118 | Candidate repair; incomplete validation | Not established |
| 119 | Candidate repair; incomplete validation | Not established |
| 120 | Candidate repair; incomplete validation | Not established |
| 121 | Candidate repair; incomplete validation | Not established |
| 122 | Prior source fix; UAT unverified | Not established |
| 123 | Candidate repair; incomplete validation | Not established |
| 124 | Prior source fix; UAT unverified | Not established |
| 125 | Prior source fix; UAT unverified | Not established |
| 126 | Prior source fix; UAT unverified | Not established |
| 127 | Candidate repair; incomplete validation | Not established |
| 128 | Candidate repair; incomplete validation | Not established |
| 129 | Candidate repair; incomplete validation | Not established |
| 130 | Candidate repair; incomplete validation | Not established |
| 131 | Candidate repair; incomplete validation | Not established |
| 132 | Candidate repair; incomplete validation | Not established |
| 133 | Candidate repair; incomplete validation | Not established |
| 134 | Prior source fix; UAT unverified | Not established |
| 135 | Candidate repair; incomplete validation | Not established |
| 136 | Candidate repair; incomplete validation | Not established |
| 137 | Prior source fix; UAT unverified | Not established |
| 138 | Candidate repair; incomplete validation | Not established |
| 139 | Prior source fix; UAT unverified | Not established |
| 140 | Candidate repair; incomplete validation | Not established |
| 141 | Candidate repair; incomplete validation | Not established |
| 142 | Candidate repair; incomplete validation | Not established |
| 143 | Prior source fix; UAT unverified | Not established |
| 144 | Candidate repair; incomplete validation | Not established |
| 145 | Candidate repair; incomplete validation | Not established |
| 146 | Prior source fix; UAT unverified | Not established |
| 147 | Candidate repair; incomplete validation | Not established |
| 148 | Candidate repair; incomplete validation | Not established |
| 149 | Candidate repair; incomplete validation | Not established |

## Connected follow-up

- All74 returned native workflows passed on the preceding candidate b8f41d80. This follow-up requires fresh native checks.
- The TLS provisioning script now covers every basic HTTP listener, including catch-all routing, and refuses unknown path maps or an untrusted HTTPS target. Three regression tests pass. The Test gateway was updated; IP, alternate-host and canonical-host probes returned301 redirects preserving path/query, and HTTPS health remained200.
- Migration130 is packaged with digest verification and content-free database postconditions. The private-network runner, workflow and approval paths are unchanged; a constrained registration test rejects unrelated builder edits. All24 migration assertions pass locally. Live migration130 execution remains pending.
- The credential-transition utility defaults to read-only verification, validates old envelopes/fingerprints, and performs both Microsoft-store updates in one transaction. Rotation requires an explicit maintenance marker. Nine isolated tests cover successful rotation, incorrect key/maintenance rejection, rollback on partial failure, and secret-free output. Native CI is required before rotation.
- Azure provisioning now requires separately provisioned runtime credentials; VM example guidance no longer reuses the database administrator. Live role provisioning and runtime validation remain pending.

## Release and identity boundary follow-up

- Release image-build dependencies no longer receive repository-content write authority. A separate fresh runner validates tag/commit/image identities before publishing the digest manifest. Four tests reject malformed tags, retagging and incorrect image identities; the publication workflow is hash-bound by the repository security gate.
- Twenty-two additional production middleware/database checks reject non-Super-Administrator edits to protected accounts across email, profile, role, password, deactivate, delete and bulk routes, including alternate route spellings. Disabled protected accounts remain protected. Total privileged integration checks:38, passing locally.
- Security middleware now shares canonical database connection resolution with session authority. The new protected-account fixture exposed the prior mismatch; missing configuration still fails closed.
- The private-network credential verifier successfully decrypted and fingerprint-validated both existing Test Microsoft stores without emitting values or changing credentials. Two independent keys are staged in the Test vault; activation and rotation remain pending.
- Proposed runtime database-role provisioning passes11 isolated privilege tests, including immutable evidence, denied TRUNCATE, no migration writes, hidden transition backups and rejection of elevated pre-existing roles. This does not establish successful application execution under that identity.
- Fresh CI remains mandatory. All existing migration-package assertions are retained, with migration130 digest/tamper coverage added. No application source deployment has occurred.

## Mixed-scope and canonical-route verification

- Approval Center now filters both aggregate hours and entry details to owned projects for project-manager scope. Eight database cases verify mixed-project days, private leave, search filtering, unrelated managers and retained authorized whole-day access.
- Actual middleware checks reject delegated system-authority secret writes and client-selected privileged import roles across normal, trailing-slash and mixed-case paths. Twelve database-backed checks reject foreign intake mutations across four GUID spellings after route-level admission succeeds.
- Session-extension SQL rejects password-rotated and absolute-expired sessions and caps successful extension at12 hours. Identity/session database checks now total18; privileged integration checks total56.
- Eight actual HTML/legacy-DOC parser cases complete within a five-second budget for ordinary content and2MB adversarial inputs. Canonical import, closeout and SSO middleware checks total9.
- Runtime database roles were prepared successfully in Test after a rolled-back rehearsal. Both remain unable to log in; the API still uses its existing account. Dedicated-key activation, encrypted-secret rotation, application execution under the runtime identity and protected UAT acceptance remain pending.
- Historical UAT artifact inventory confirmed retained evidence archives from prior successful runs. Their private review and containment remain open; no cleanup is claimed by these source tests.

- The private credential-transition utility now includes a maintenance-gated reverse transition. Fifteen isolated checks cover rotation, atomic failure, restoration to the previous key derivation and successful rotation again. Native validation is required before live use.

## Protected reset and operational authority

- Password-reset completion calls a directly tested protected-target guard and locks the approved request/local-account records before replacing credentials. Break-glass accounts remain blocked even for Super Administrators; View-As and non-Super-Administrator protected-target resets are rejected.
- Actual maintenance handler and CI/CD authorization tests reject delegated system authority, while retaining administrator admission. Privileged integration checks now total72 locally; fresh native validation is required for this follow-up.
- The preceding source checkpoint passed the native security suite including15 atomic credential-transition cases. A failed artifact-upload network request was retried without changing validation requirements.
- Dedicated integration keys are now active in Test after an atomic two-store transition and new-key verification. HTTPS health returned200 on the configuration revision. The application image/source remains the existing release, not this PR.
- Identified historical Protected UAT artifacts were privately preserved, checksum-verified and removed from repository-readable storage. Verification confirmed those URLs unavailable. Other historical exposure and credential rotation remain part of the restricted review.
- Runtime database credentials are staged in Test; application identity activation and live acceptance remain pending. Migration130 has not run in UAT. No finding is declared closed.
