# Module 025 browser entry verification

Installed verification run 35256055504, job 105319887564, failed on 2026-09-17
with `module025_browser_timeout_workspace`. Artifact 10512895744 (ZIP SHA-256
`d82d576301098ac55b4b35e32c0a42b18ecfb59fba9cfe4e7bedfcc2b5007afa`) records
zero generation requests, zero browser business writes, zero page errors, and
no failed Module 025 HTTP responses. My Role passed. The installed application
is `3b7185292c013cd032b87ce6764ea238813e9e7a`; this run did not deploy anything.

The artifact proves that browser entry failed; it does not identify whether
the live page was redirected, still loading, or hidden. Do not attribute this
failure to an AI provider or claim a proven live root cause.

## Reproduction and correction

The complete production build renders two Module 025 authoring workspaces:
the legacy EnterpriseModulePresentation copy is hidden by the enterprise
stylesheet, and SalesDeliveryWorkflowCenter owns the visible copy. The old
unqualified locator produces a Playwright strict-mode error against this
actual built page. A component-only fixture did not expose this defect.

The verifier now requires one **visible** workspace. It does not select the
first match or tolerate two visible workspaces. The application, permissions,
generation code and deployment controls are unchanged.

The browser regression now serves `npm run build` output, including the real
main entry point, shell, generated App, styles, permission bridge and route
wrappers. Synthetic local API responses supply an ordinary SA session; no
provider or live Test service is called. The real Python verifier exercises
preflight, both repeated retained-document downloads, reopening, saving,
reload and record selection. Negative cases exercise denied downloads and
an explicit Module 025 permission denial through the actual route guard.

Each browser attempt retains only fixed page-state labels, booleans, counts,
and bootstrap HTTP statuses. Failures print these diagnostics directly in
Actions. No DOM, arbitrary URLs, customer content, credentials, response
bodies or session values are retained. The denied-module regression confirms
that a redirect to the dashboard fails before generation and reports the
denial without bypassing it.

## Validation and release boundary

Local complete-application browser replay passed, including the negative
cases. The frontend production build passed. GitHub CI must independently
pass on the published commit before merge.

This is a verifier-only correction. It needs no application redeployment and
does not establish live functional acceptance. The existing installed
verifier can still target deployment 35249650844 with `acceptance_scope=sow_role`.
Its preflight remains ahead of generation. If entry still fails, use the new
route/permission/loading evidence before another provider call or code change.
SELL publication remains separately unverified and blocked by the missing
document-write adapter; a SOW browser pass must not claim SELL success.
