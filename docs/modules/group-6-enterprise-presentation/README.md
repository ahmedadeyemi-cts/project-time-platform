# Group 6 — Shared Page Structure and Official US Signal Branding

## Purpose

Group 6 creates one reusable enterprise presentation system for ProjectPulse rather than independently redesigning each module. The system uses the official US Signal logo image already approved in the repository and adopts the current US Signal public-site presentation principles: confident navy foundations, cyan accents, strong whitespace, readable typography, restrained status color, and enterprise-scale page widths.

This package standardizes presentation only. Existing APIs, data ownership, permissions, routes, mutations, and module workflows remain authoritative.

## Adoption scope

The route-aware presentation is enabled for:

- **Module 024** — Sales Intake
- **Module 025** — SOW Generator
- **Module 027** — Signed Handoff
- **Module 028** — AI Time Entry
- **Module 029** — UAT Validation
- **Module 064** — AI Provider Configuration Center
- **Module 068** — Provider-Neutral System Architecture
- **Module 069** — Qualifications & Certification Matrix
- **Module 071** — On-Call Scheduling
- **Module 072** — OneAssist Routing Directory
- **Module 074** — OEM & Vendor Directory

## Reusable components

`EnterpriseModulePresentation.jsx` exports:

- `EnterprisePageHeader`
- `EnterpriseModuleLabel`
- `EnterpriseSummaryStrip`
- `EnterpriseStatusCard`
- `EnterpriseFilterBar`
- `EnterpriseTabs`
- `EnterpriseTable`
- `EnterpriseEmptyState`
- `EnterpriseWarning`
- `EnterprisePrintHeader`
- `EnterpriseModulePage`

`USSignalLogo.jsx` is the official reusable US Signal logo component. It consumes the repository's single approved `usSignalLogoDataUrl` asset. Hanging text, generated wordmarks, alternate embedded images, and fabricated logo treatments are not accepted as substitutes.

## Route integration

The Group 6 installer makes one additive change to the generated application shell:

1. Import the reusable `EnterpriseModulePresentation` component.
2. Mount it once after the existing `PageContextGuide`.
3. Resolve the active target module from `activeRoute`.
4. Render no Group 6 content for routes outside the approved adoption set.

This avoids eleven independent App rewrites and avoids changing module-specific business logic. Existing route classes and IDs continue to own the operational content beneath the shared header and summary strip.

## Presentation contract

The shared system provides:

- official US Signal logo rendering;
- one enterprise page header;
- visible module number and functional group;
- summary/status strip;
- reusable filter bar;
- reusable tabs;
- reusable responsive table;
- consistent empty states;
- consistent warnings and critical errors;
- responsive page width;
- consistent typography;
- visible keyboard focus;
- accessible status contrast;
- constrained horizontal table scrolling; and
- print/export header behavior.

## Functional boundaries

### Permissions

No permission change is included. Group 6 does not add roles, permissions, module visibility, route authority, or mutation authority. Each module continues to use its current server and frontend permission checks.

### More menu

The permission-aware More menu is excluded. Group 6 does not modify its routes, labels, grouping, filtering, or security behavior. That responsibility remains with Group 1.

### Data and APIs

No backend endpoint, database query, provider adapter, or operational workflow is changed. Presentation components do not fetch data and do not become an alternate source of truth.

### Migration

No migration is required or included.

### Deployment

No deployment workflow, Azure resource, Container Apps resource, Test resource, Production resource, credential, or external provider is changed. No deployment is performed by this source package.

## Shared-file overlap

The only existing shared file changed by Group 6 is frontend `package.json`, where the idempotent installer and validator are added to `predev`, `prebuild`, and the complete production build chain.

Canonical `App.jsx` is not committed by Group 6. It is generated through the established predevelopment/prebuild process. Module registries and navigation files are not rewritten.

## Validation

The Group 6 validator confirms:

- the official image asset is used;
- all reusable components exist;
- all eleven target modules have presentation metadata and route adoption;
- typography, contrast, focus, responsive, table-scroll, and print contracts exist;
- one App import and one route-aware presentation mount are installed;
- existing routes remain present;
- the More menu and module registry are untouched;
- no text-only logo substitute is introduced; and
- Group 5 and all existing complete-build validators continue to run.
