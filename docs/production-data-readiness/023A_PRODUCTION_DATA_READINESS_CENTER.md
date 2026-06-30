# 023A Production Data Readiness Center

## Purpose

023A adds a webpage-visible Production Data Readiness Center and backend endpoint.

## Webpage

`https://projectpulse-test.onenecklab.com/#production-data-readiness`

## Dashboard / Navigation

The module is added as:

- Title: Production Data Readiness Center
- Navigation label: Data Readiness
- Group: System Operations

## Backend Endpoint

`GET /api/production/data-readiness`

## Validation

After deployment, verify:

1. The module is clickable from Dashboard/navigation.
2. Direct route loads without blank screen.
3. Refresh data readiness returns endpoint status.
4. Table shows data areas, backend table names, counts, status, purpose, and webpage validation guidance.
5. Links navigate to the related app pages.

## 023B Production Data Remediation Checklist

023B adds a remediation checklist inside the Production Data Readiness Center.

What to check:

1. Non-ready rows appear as remediation items.
2. Items can be checked off.
3. Notes persist after refresh.
4. Copy remediation plan provides a text summary.
5. Reset checklist clears local validation state.

## 023C Production Data Go-Live Gate

023C adds a go-live decision panel inside the Production Data Readiness Center.

What to check:

1. Go-live gate appears after refreshing data readiness.
2. Backend blockers, missing tables, needs-data review, and checklist completion are summarized.
3. Go-live checklist items can be checked off.
4. Decision notes persist after refresh.
5. Copy go-live evidence provides a text summary.
6. Reset go-live gate clears local validation state.
