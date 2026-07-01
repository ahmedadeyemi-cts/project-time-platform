# 028 SOW-Aware AI Time Entry Generator

## Status
Applied as complete Module 028 pending validation and commit.

## 028A SOW/GSD Scope Context Registry
Adds scope context for signed SOW, GSD, assignment, intake, CRM, customer, project, task, and engineer visibility.

## 028B Engineer AI Time Entry Draft Workspace
Adds Engineer workspace where rough work descriptions can be turned into AI draft time entries.

## 028C Scope Alignment Checker
Adds in-scope / likely-in-scope / needs-review / out-of-scope / insufficient-context model.

## 028D Claude Provider Readiness Hook
Positions Claude as server-side only. No AI provider key is stored in the repository.

## 028E Engineer Review / Accept / Edit Controls
AI output is draft-only. Engineer must review, edit, and accept before final use.

## 028F Audit Trail
Captures original engineer input, AI draft, final engineer entry, accepted hours, SOW/GSD version, actor, and timestamp.

## 028G Role Enforcement
Engineer can draft only own assigned project/time entries. PM, Team Lead, PTC, Admin, and Executive receive scoped visibility based on role.

## 028H Closeout
Adds readiness checklist and module closeout evidence.

## Database Foundation
Adds:

- `sow_ai_time_entry_scope_contexts`
- `sow_ai_time_entry_scope_documents`
- `sow_ai_time_entry_ai_provider_readiness`
- `sow_ai_time_entry_drafts`
- `sow_ai_time_entry_scope_checks`
- `sow_ai_time_entry_acceptance_events`
- `sow_ai_time_entry_role_rules`
- `sow_ai_time_entry_readiness_reviews`

## Workflow Placement
- Module 024 validates intake readiness.
- Module 025 provides signed SOW context.
- Module 026 provides CRM-originated context.
- Module 027 provides signed handoff and assignment context.
- Module 028 enables SOW/GSD-aware AI time-entry drafting for assigned Engineers.
- Module 029 should validate role and workflow enforcement.
