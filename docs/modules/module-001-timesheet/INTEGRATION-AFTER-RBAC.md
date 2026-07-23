# Integration sequence after scoped RBAC PR #83

1. Wait for PR #83 to merge and record its merge SHA, migration status, and deployed status.
2. Create a fresh Module 001 integration branch from the new `origin/main`.
3. Replay this additive Phase 0 work or cherry-pick only its Module 001-owned files.
4. Resolve the final task repository and timer schema from current main.
5. Assign the next available migration number after inspecting the merged migration ledger.
6. Add backend endpoints and enforce scoped authorization, self-only timer ownership, and View-As denial.
7. Register components in the current Module 001 shell without replacing working views.
8. Rename only user-facing Module 001 labels to Timesheet.
9. Wire a dedicated protected validator into package and CI after revalidation.
10. Open a draft PR, run full CI, obtain explicit merge approval, migrate test only, deploy test, and complete UAT.
