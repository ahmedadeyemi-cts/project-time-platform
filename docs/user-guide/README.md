# Pulse user guide ownership and coverage

The live System User Guide is Module 999 (`#user-guide`). Its authored content is in `src/frontend/project-time-web/src/guide/`; the generated Module 001 foundation remains owned by the existing Timesheet build generator. Neither the handbook nor its role reference grants permissions.

## Current edition

Edition 2026.09.22 reviews implementation baseline `8d0baeb69b9594b30dc203f12e729e482e758f46`: 76 workspace topics (73 numbered workspaces and 3 platform routes), 99 module how-to procedures, 16 role handbooks, 13 platform how-tos and 5 cross-team handoff flows. The build-injected Module 084 is explicitly included. The guide uses canonical module identities rather than duplicate legacy smoke-test labels.

The guide covers worker time/timers/corrections, approval stages, project creation and engineering delivery, source-specific customer and commercial records, six SOW/GSD authoring/review/transfer/template procedures, sequential FlowHive planning, project financials and closeout, reporting, integration and AI setup, operational/security governance, resource/on-call/lab/risk work, and the status distinctions needed to use them safely. Each module includes responsibility, prerequisites, steps, expected outcome, next owner, limitations and implementation source paths. The role reference reuses existing role playbooks with explicit current creator boundaries; it does not alter those roles or their visual stories.

## Keeping it current

1. Update the appropriate authored `guide-*.js` topic when an implemented control, prerequisite, record state, or role responsibility changes.
2. Include the actual source paths; do not promote a requested, unconfigured or draft-only feature to a working integration.
3. Run `node scripts/validate-user-guide.mjs` from the frontend directory. New static or App-installed routes without reviewed procedures fail coverage. Unknown runtime-only routes are displayed as missing reviewed instructions, not silently counted as covered.
4. Run the full frontend build and the browser guide check. The build must preserve source stability and existing Module 001, Help governance, numeric ordering, role-story, contrast and security checks.
5. Generate the complete Markdown reference from the same data with `node scripts/export-user-guide.mjs --output /path/to/PULSE-USER-GUIDE.md`. CI retains this reference, the coverage report, browser results and screenshots. Do not maintain a separate manually edited copy.
6. Follow normal review and protected deployment. A successful guide build is not a production deployment authorization.

## Access and evidence limits

My responsibilities comes from the existing verified role context, including identity-change and View-As handling. All role reference is explicitly learning material; selecting it never assigns a role. Workspace links in the new how-to library use verified permitted routes, not the static audience catalog or a Super Administrator label. Existing APIs remain the final action authority.

Coverage tests verify documented route/role inventory, content structure, source references, generator contracts, search/filter behavior, and deny-by-default link decisions. Browser tests run the actual generated guide with production CSS against synthetic local identity data, with external requests blocked. They are not exhaustive live account-by-account or external integration acceptance. Configure and verify integrations in their owning modules, preserve draft versus saved versus published distinctions, and escalate missing evidence.

The existing Protected UAT release process independently verifies installed release identity, application health, the assigned Project Manager story and saved SOW/GSD exports. Its selected acceptance scope must not be described as full AI-generation, SELL-publication, or all-role testing.
