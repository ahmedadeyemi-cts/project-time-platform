# AZ-12B7 Work Register persistence and Time Entry repair

Base source: `abf45bf824747767282f68fa5bd50909f9751eb0`

## Corrected behavior

- Final intake commit persists Contract Type, SELL Quote, Salesforce ID, Certinia ID, Signed SOW Date, and canonical Work Type.
- Work types are normalized to `Project`, `IQS`, `Service Request`, `Pre-sales`, `Internal Project`, or `Other`.
- Intake-created documents receive durable display names such as `GSD_Staff Aug` and `SOW_Staff Aug`; the original uploaded filename remains separate.
- Work Register task assignment history is synchronized into `project_assignments`, the source used by `/api/assignments/available-tasks`, both during intake commit and during later roster edits.
- Time Entry routes Project/IQS tasks to Regular Tasks and all other supported work types to Requests / Service Requests. The lists are mutually exclusive.
- Review-save failures stop final commit.
- Multi-engineer roster edits use `taskRosterForms.rows`, matching the state consumed by the roster UI and save operation.
- Legacy post-commit identifier endpoints enforce the same PTC/PM/Admin authorization boundary.

## Deployment order

1. Back up the Azure PostgreSQL database or take a point-in-time restore marker.
2. Apply `projectpulse-055d7-intake-finalization-time-entry.sql` with `ON_ERROR_STOP=1`.
3. Run `projectpulse-055d7-verify-readonly.sql`.
4. Build the API and frontend from the patched source.
5. Deploy configuration/images only after both builds pass.
6. Perform one fresh intake test for each canonical work type.

The SQL migration includes an idempotent backfill for existing committed intake packages. It repairs the first successful Azure Files intake without requiring that project to be recreated.

## Acceptance checks

For a fresh Project or IQS intake:

- Work Register badge shows the selected type.
- Contract Type and all supplied external identifiers are visible after refresh.
- Registered documents show `GSD_<Project Name>` and `SOW_<Project Name>`.
- Assigned engineers see their specific task under Regular Tasks.

For Service Request, Pre-sales, Internal Project, or Other:

- Work Register badge shows the selected type.
- Assigned tasks appear only under Requests / Service Requests.
- The same task does not appear under Regular Tasks.

For a forced review-save failure:

- The intake drawer stays open.
- Entered values remain present.
- A red error status is shown.
- No project is committed.

## Rollback

The rollback script removes the trigger, helper functions, and supporting indexes. It intentionally preserves added columns and repaired business data to avoid destructive rollback behavior. The previous source revision can safely ignore the retained columns.

## Validation completed in this package

- Original bundle SHA-256 verified: `1167b56a6b87ec39965eacc7548449f12a6d4d7a046190f685b40bb076fd78d7`.
- JavaScript/JSX syntax bundled successfully with esbuild for both modified frontend files.
- C# syntax parsed successfully with tree-sitter: zero error nodes.
- PostgreSQL migration parsed successfully with pglast: 20 statements; verification and rollback scripts also parsed successfully.
- A full .NET compile was not available in the artifact environment because the .NET SDK was not installed.
- A full Vite application build was not possible from the user-provided source bundle because it intentionally omitted `index.html` and the other local frontend modules. The modified JSX files themselves passed syntax bundling.
