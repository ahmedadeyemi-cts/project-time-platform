# 025A SOW & GSD Workspace

Module 025 gives Solution Architects a persistent workspace (Sales & Opportunities → SOW &
GSD Workspace) to author, review, and export Statements of Work and General Solution
Designs.

## What it does
- Create a SOW/GSD record for a customer, a commercial model (Time & Materials or Fixed
  Price), and a customer program (Standard, Toyota, or Hyundai).
- Describe the engagement in a Service Overview, then generate a detailed
  Plan/Design/Implement/Validate/Release scope with Celar AI. AI-suggested hours stay
  separate from your reviewed final hours.
- Edits autosave (roughly every second) with revision-conflict protection.
- Confirming the reviewed package unlocks SOW (`.docx`) and GSD (`.xlsx`) downloads.
- Archive/unarchive removes or restores a record from the active work queue without
  deleting it.

## Access
- Solution Architects see and edit their own records.
- Managers and team leads get read-only visibility into their direct reports' records.
- Administrators have full access; Administrator View-As is always read-only.

See
[docs/production/025_SOW_GENERATOR_CLAUDE_REVIEW_WORKFLOW.md](../production/025_SOW_GENERATOR_CLAUDE_REVIEW_WORKFLOW.md)
for the full technical workflow.
