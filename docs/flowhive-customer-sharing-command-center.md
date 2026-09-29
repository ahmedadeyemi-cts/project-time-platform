# FlowHive customer sharing and Project Home

## Reported behavior and evidence

The user reported `request_validation` reference `PP-3B56E06D` and HTTP400 from the project `/controls` PUT when clicking **Enable customer sharing for this project**. The supplied console excerpt did not include that request's JSON payload or the response body, so the exact rejected production field is not established.

Source inspection confirms that the old enable button copied the entire local financial-controls form to that endpoint. The local form includes unrelated commercial inputs and readback metadata. A real, isolated ASP.NET binding test reproduces HTTP400 with the existing request record when an unrelated nullable budget is the empty string; a JSON null binds successfully. Unknown metadata alone is not claimed as the cause. This PR removes financial-form binding from the sharing action rather than broadening global JSON validation or changing stored financial values.

## Request and persistence changes

`POST /api/project-flowhive/projects/{projectId}/customer-sharing/enable` is an explicit enable command. Its existing project-scoped CustomerShare authorization is authoritative. Body-supplied project IDs, budgets, permission flags and notes are not inputs to the mutation. A missing session is rejected; authorized sharing roles remain assigned PM, authorized PM lead or administrator, outside View-As.

The transaction updates only the project's sharing flag and update actor/time. Existing budget, currency, commercial model, notes and reporting cadence remain unchanged. A project without controls uses the existing migration086 defaults. Repeating an already-enabled request reports no state change; concurrent requests transition only once. A changed transition is audited in the existing FlowHive audit table. No schema migration is needed.

The command never creates a token, customer link, baseline, approval or notification. Link creation remains a second explicit action against an exact reviewed baseline and retains current expiry, revocation and customer-safe projection rules. This PR does not enable sharing for a live project during development.

The financial save remains available separately and now builds an explicit ten-field write DTO, converting empty nullable money fields to null and rejecting invalid numeric inputs locally. It applies confirmed response controls only for the selected project.

## Project Home design

The Project Command Center now includes a customer collaboration card, also reused in Branded Exports. It explains the distinction between enabling the project and granting access through a customer link. States cover internal-only, enabled with no active links, active links, loading, failure and saving. If previously issued links remain active while new link creation is disabled, the card explicitly says so and keeps authorized revocation available; it never labels that state internal-only. Review prerequisites, privacy exclusions and a local error reference are visible alongside the action.

The button uses the actual `canShare` capability, not generic `canManage`. A confirmation explains that enablement does not create/send a link. Pending requests are disabled, failures never optimistically enable, and project/selection checks reject stale replies. Changing projects clears the one-time URL, share draft, customer note and inline error. A sharing save does not reload over unsaved commercial edits.

The command center has four summary cards, readable dates, closed-work progress, spaced quick actions and compact work-filter chips instead of a second row of large duplicate cards. Task attention filters, ordering and current task/schedule calculations are retained. No fabricated due dates or automatic approvals are introduced.

Scoped CSS supports light/dark and narrow layouts. Headers use foreground tokens rather than dark brand backgrounds as text. Browser checks measure title, status and eyebrow contrast and test page-width containment at 390px and 1440px. Screenshots use synthetic data and the application's root font stack, not live customer data.

## Validation

- `node --test tests/flowhive-sharing/controls.test.mjs tests/flowhive-psa-overview.test.mjs`: request construction, invalid values, permission/View-As gates, project switching, failures and overview semantics.
- `dotnet run --project tests/FlowHiveCustomerSharingTests -c Release`: actual ASP.NET old/new route binding and anonymous authorization on loopback, without starting the application or background workers.
- Add `-- --database` only with `FLOWHIVE_SHARING_DB` pointing to the named disposable `flowhive_sharing_test` database on 127.0.0.1. The actual store is exercised against the existing controls table contract, including finance preservation, idempotence, other-project isolation, defaults, rollback and concurrency.
- `node tests/flowhive-sharing/browser-bundle.mjs` and `python tests/flowhive-sharing/browser.py`: actual full Center React requests and interactions; all network requests intercepted with explicit synthetic fixtures. Tests cover success, HTTP400, delayed reply/project switch, engineer/View-As restrictions, baseline gating and both themes/layouts.
- Full API/frontend builds and existing repository checks remain required.

The new read-only CI workflow publishes only synthetic browser screenshots. No live secrets, application sessions, customer links or production/UAT write calls are used.

## Review and UAT acceptance

This is a new application PR only. It does not alter deployment workflows, protected environment rules, restricted database identities, notification policies, or existing release gates.

After normal review and a separate authorized Protected UAT deployment, test with an authorized PM on a controlled project. Confirm enablement succeeds without changing financial values, no link exists merely from enablement, and refreshed project state remains enabled. Check unauthorized/engineer/View-As identities are denied; link creation still requires an approved baseline. Verify create/copy/revoke separately with controlled customer-safe artifacts.

The original customer's exact error reference still needs deployment-time confirmation. Source tests and an HTTP400 binding reproduction are not represented as a verified live production root cause or completed tenant acceptance.
