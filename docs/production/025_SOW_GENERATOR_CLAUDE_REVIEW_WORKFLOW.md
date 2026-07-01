# 025 SOW Generator + Claude Review Workflow

## Status
Applied as a full demo-ready frontend workflow suite.

## 025A Dashboard + SOW Workspace
Adds the SOW Generator module to the dashboard and opens the `#sow-generator` workspace.

## 025B Word Template + Field Mapping
Adds Word template placeholder controls, template metadata, and field mapping preview. Backend Word generation will be wired later.

## 025C Claude Draft Studio
Adds Claude-assisted draft generation controls, section-by-section regeneration, editable draft sections, and prompt context preview. The live Claude provider will use the existing shared API key pattern already used by the timesheet generator.

## 025D Solution Architect Review + Signed Handoff
Adds human review controls, hallucination checklist, signed SOW/GSD readiness checklist, and Sales-to-Delivery handoff preview.

## 025E Project Document Visibility
Keeps SOW/GSD documents tied to the existing Project Hours / SOW-GSD / Engineer Allocation area. PMs, Engineers, and Leads should access the same canonical documents through Project Workspace / Engineering Documents.

## Design Rule
One SOW/GSD document record. One uploaded file. Many controlled views. No duplicate SOW/GSD upload location.

## Later Backend Wiring
- Shared Claude provider endpoint.
- Word template parsing and `.docx` generation.
- Canonical SOW/GSD document lookup.
- Signed SOW/GSD upload enforcement.
- Sales-complete trigger.
- Email to PTC and Executive.
- PM/Engineer assignment email with SOW/GSD.
- SOW-aware AI time-entry scope validation.

## 025F Dashboard Placement Fix

Status: Applied pending validation and commit.

Scope:
- Move SOW Generator card into the dashboard module card grid.
- Prevent orphan rendering above the application header.
- Keep the SOW Generator workspace attached to the dashboard grid.
- Hide the page context helper panel for the demo view.

## 025G Stable Demo Fix

Status: Applied pending validation and commit.

Scope:
- Customer field changed to onboarded-customer dropdown with API fallback.
- Project Type now includes Service Request.
- Added Save Signed Handoff + Trigger Email workflow control.
- Signed handoff requires customer, project, SA review, signed SOW, GSD, and canonical document review.
- Download draft now creates a Word-compatible `.doc` file.
- Uploaded Word template becomes the selected template reference.
- Claude prompt now instructs process-aligned scope and deliverable drafting based on the SA description.
- Replaced previous endless-scroll rendering with stable one-time rendering and limited retries.

## 025H Demo Hardening

Status: Applied pending validation and commit.

Scope:
- Customer dropdown is seeded from existing Customer Directory database records during deployment.
- Project Type includes Service Request.
- Replaced prior Module 025 injected scripts with a bounded, stable renderer to stop endless scrolling.
- Save Signed Handoff + Trigger Email validates customer, project, SA review, signed SOW, GSD, and canonical document review.
- Download Word Draft produces a Word-compatible document.
- Uploaded Word template becomes the selected template reference.
- Claude prompt preview instructs process-aligned scope and deliverable drafting from the Solution Architect description.

## 025I SOW Route Isolation

Status: Applied pending validation and commit.

Scope:
- Isolate `#sow-generator` as a focused route.
- Hide dashboard cards, including Notifications and the Module 025 launch card, while viewing the SOW Generator workspace.
- Keep the SOW Generator dashboard card visible on `#dashboard`.
- Stop the visual endless-scroll behavior caused by dashboard cards and SOW workspace rendering together.

## 025J Parse-Safe Final Demo

Status: Applied pending validation and commit.

Scope:
- Removed all prior duplicated/broken Module 025 injected blocks.
- Replaced them with a single parse-safe Module 025 demo block.
- Customer dropdown is seeded from Customer Directory database records.
- Project Type includes Service Request.
- `#sow-generator` is route-isolated so dashboard cards do not display above the SOW Generator.
- Download Word Draft produces a Word-compatible `.doc`.
- Signed handoff validates customer, project, Solution Architect, signed SOW, GSD, SA review, and canonical document review.
- Claude prompt preview instructs process-aligned scope and deliverable drafting.

## 025K Research-Backed SOW Demo

Status: Applied pending validation and commit.

Scope:
- Recovered Module 025 into a single parse-safe block.
- Added Research Actual Delivery Process workflow.
- Added Process Research Brief section.
- SOW draft generation now uses the research brief to create project scope and deliverables.
- Customer dropdown is seeded from Customer Directory database records.
- Project Type includes Service Request.
- `#sow-generator` is route-isolated so dashboard cards do not display above the SOW Generator.
- Download Word Draft produces a Word-compatible `.doc`.
- Signed handoff validates customer, project, Solution Architect, research review, signed SOW, GSD, SA review, and canonical document review.

## 025L Newline Format Fix

Status: Applied pending validation and commit.

Scope:
- Fixed research brief and generated SOW sections so they render real line breaks instead of literal `\n`.
- Added browser-state normalization so previously saved draft content is cleaned up on load.
- Ensured Word-compatible download uses readable line breaks.

## 025M Standalone SOW Route

Status: Applied pending validation and commit.

Scope:
- Replaced all prior Module 025 injected blocks with a standalone route shell.
- `#sow-generator` now displays as a focused full-page workspace overlay instead of rendering inside the dashboard grid.
- Prevents the underlying app route, notifications card, dashboard card, and user-admin page from appearing behind or above the SOW Generator.
- Keeps the Module 025 dashboard card available on `#dashboard`.
- Keeps research-backed SOW generation, customer dropdown, Service Request project type, Word download, and signed handoff validation.
