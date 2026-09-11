# Module 999 companion page — My Role in Pulse

## Purpose and navigation

**My Role in Pulse** is a dedicated, US Signal-branded learning page at `#my-role-in-pulse`. It is not a drawer or a section buried inside the module reference guide.

The authenticated top navigation includes a direct link. The existing System User Guide includes a launch card and remains separately available at `#user-guide`. The dedicated route is an alias of Module 999 for authorization and module availability; it does not allocate a duplicate module number or grant additional roles.

## User experience

1. Choose a role or search for a role/task. Effective assigned roles appear first and are marked **Your role**. Learning about another role does not impersonate that role.
2. Read the role-level story, responsibility, boundary and upstream owner.
3. View the shared project lifecycle with this playbook's touchpoints highlighted.
4. Select a numbered step in a connected graphical flow. Each step includes its own story and **What I need → What I do → What I produce**, followed by **Ready when**, **Who receives it next**, and an exception path.
5. Open the existing authorized workspace or walk through a clearly fictional example. Previous/Next navigation changes only the displayed learning step.

The first content version contains 15 role playbooks and 60 task-level steps: Sales/Account Executive, Solution Architect, Inside Sales Representative, Project Manager, Project Management Lead, Engineer, Engineering Lead, Manager, Project Team Coordinator, Project Coordinator, Accounting, Billing/Finance, Executive, Administrator and Super Administrator.

The page combines maintained stories with the existing `ROLE_GUIDANCE` catalog and the effective user's assigned role codes. This is not a claim to enumerate every live database role. A new catalog or assigned role without a maintained playbook appears with an explicit guidance gap. Do not silently assign it another role's workflow or authority.

## Important content boundaries

- These are **draft operating playbooks requiring business-owner review**, not declarations that every workflow has passed UAT or that a real project has completed a step.
- SOW/AI output remains a draft until authorized review. Missing source material must not be invented. Planner work breakdown and milestones remain distinct.
- Module 001B administrative reallocation is distinct from a worker-return correction workflow. The administrative path does not force Draft, worker resubmission, or new Manager/PM approval. PTC does not submit another person's timesheet.
- Project Coordinator is not automatically a PTC time steward. Administrator is not silently presented as Super Administrator.
- Missing costs are unknown, not zero. Billing rates are not internal labor costs. A downloaded or queued package is not evidence of posting, delivery or reconciliation.

## Architecture and integration ownership

- `src/role-journeys/role-journeys.js`: versioned content and pure catalog/search/validation functions.
- `MyRoleInPulse.jsx`: React-owned page and visual flows. No separate root, runtime DOM insertion, operational writes or AI requests.
- `use-role-journey-context.js`: consumes the existing effective role and authorized module-navigation helpers. Links are withheld while authority is not ready. The snapshot contains no credentials or customer records.
- `RoleJourneyGuideRouter.jsx`: preserves the existing generated guide and selects the dedicated page using the browser hash.
- `scripts/role-journeys-vite-plugin.mjs`: narrowly scoped, idempotent pre-transform for the generated App and the module route-alias registry. Missing or duplicated anchors fail closed. Build startup validates story completeness, target module routes and source integration anchors.
- `vite.config.js`: one plugin import and one plugin registration; all existing source transactions, FlowHive browser-contract checks and other plugins remain intact.

The App resolves the dedicated hash through the existing `user-guide` route. The browser URL remains `#my-role-in-pulse`, and the companion router chooses the dedicated page. Module availability recognizes the alias as Module 999. Existing login, password-change, frontend scope, module availability and server-side authorization remain authoritative.

No changes to the generated files are committed. No package upgrades, new runtime dependencies, database migrations, API endpoints, role-policy grants, operational records, AI provider configuration, deployment workflow or environment settings are included.

## Validation

Run from `src/frontend/project-time-web`:

```sh
node --test tests/role-journeys.test.mjs
npm run build
```

The focused Node suite includes 18 checks for content completeness, known and new roles, alias separation, multiple assigned roles, search, story strings, valid module targets, additive/idempotent routing, unchanged unrelated routes and source-level accessibility/learning boundaries.

Local evidence for the initial PR: all 18 focused tests passed; JSX/JavaScript syntax transpilation passed; CSS parsing passed. These checks are **not** a full browser test or a full application build. Full repository CI, authenticated browser verification and business-owner sign-off are release gates, not claimed complete by this document.

## Protected UAT acceptance checklist — pending deployment authorization

- Sign in and open **My Role in Pulse** directly from top navigation and from the System User Guide. Hard-refresh `#my-role-in-pulse`; use browser Back/Forward; return to `#user-guide` and confirm the original guide is intact.
- Check all 15 playbooks. Confirm stories, input/action/output cards, numbered connectors, acceptance and receiving-role handoffs update together. Exercise Previous/Next at first/last steps.
- Search for `engineer`, `handoff` and `time`; exercise a no-result query and Clear search. Confirm multiple assigned roles appear first and unrecognized assigned roles show a guidance gap.
- Test a restricted engineer, PM, accounting, PTC and administrator. Selecting a different playbook must not unlock its workspaces. Loading/denied authority withholds action links. View-As uses effective-user access; exiting View-As restores actual-user navigation.
- Verify sign-out/expired-session and forced-password-change boundaries. Disable Module 999 in a test scenario and confirm the alias cannot bypass it. Do not alter production policy for this test.
- Use keyboard only, 200% zoom, narrow/mobile width, light/dark themes and high-contrast settings. Confirm visible focus, readable steps and no clipped controls.
- Disconnect AI providers in an approved test context or inspect requests: this page must not depend on an AI call. Reading, examples and Previous/Next must not send operational writes or alter project/time status.
- Have each process owner validate the operating sequence and handoffs. Update content/version before representing the playbooks as approved guidance.

## Scope and rollback

PR only. No merge or deployment is authorized by this implementation. Production and Protected Test are not modified. Concurrent FlowHive/SOW work is not retargeted or merged into this branch.

Rollback is a normal reviewed revert of this PR. Removing the plugin registration returns the original guide import, hash parser and module alias behavior. No data migration or record recovery is required because this feature writes no business data.
