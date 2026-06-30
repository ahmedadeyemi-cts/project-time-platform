# 023C Production Data Go-Live Gate

## Webpage Impact

Adds a go-live decision panel inside the existing Production Data Readiness Center.

Open:

`https://projectpulse-test.onenecklab.com/#production-data-readiness`

This is not a separate Dashboard module. It enhances the existing clickable Data Readiness module.

## Backend Support

No new endpoint is required. The go-live gate uses the existing:

`GET /api/production/data-readiness`

## What to Check on the Webpage

- Refresh data readiness.
- Confirm the go-live gate appears.
- Confirm backend blockers, missing tables, needs-data review, and checklist completion are summarized.
- Check go-live checklist items and confirm the gate status updates.
- Enter decision notes and refresh; notes should remain.
- Click Copy go-live evidence and confirm the evidence box updates.
- Reset the go-live gate and confirm state clears.
