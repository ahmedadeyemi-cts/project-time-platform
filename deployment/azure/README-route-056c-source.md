# 056C dashboard route isolation source correction

Base source commit:

`3709e9df4833c6e7794d6262ed867c1630f1e2a6`

Source branch:

`source/invoice-billing-center-preview-20260714`

The correction removes the invalid requirement that legacy dashboard cards
must be descendants of React's `.app-shell`.

The complete frontend entry document and relevant route/runtime files were
read and inventoried before the guard was replaced. The committed audit is:

`docs/056c-dashboard-route-isolation-full-file-audit.md`

No Azure resource, API, or database change is performed by the source-fix
script.
