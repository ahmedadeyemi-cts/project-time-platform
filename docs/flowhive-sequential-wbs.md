# Sequential FlowHive planning

The durable AI Planner shared by FlowHive and Project Forge/AI Studio generates Plan, Design, Implement, Validate and Release in order. Each phase expands the same current SOW Service Overview/Scope, with GSD and eligible supporting project evidence. Earlier validated tasks supply proposed dependencies and outputs; they are not verified customer facts.

The private worker pins bounded evidence once, checks document IDs, active version IDs and hashes, and saves each validated phase before continuing. Provider failover remains governed by Module 064. There are at most four provider attempts per phase, one schema repair per provider invocation, a 330-second request cap and a 40-minute durable run deadline. Completed phases survive worker restart. Cancelled, expired, closed, source-changed or unauthorized runs cannot save a new phase or overwrite a plan. Partial output never becomes the working plan. The final WBS uses the existing validation, scheduling and PM review gates.

AI Planner/Workspace and AI Studio show five phase cards with saved start/finish timestamps alongside the overall timer. Studio observes the exact run ID, including terminal failures, instead of queueing a replacement. Timers show elapsed time, not a promised completion estimate. Public progress excludes document text and partial task content.

Intake-to-project association, intake file upload and Work Register upload queue eligible documents transactionally under the actual actor. Other project attachments now participate as supporting evidence, preserving visibility, AI consent, malware scanning and supported-format checks. Files may still require processing time; the planner waits for preparation rather than requiring another upload. Failures remain visible and do not claim readiness.

Migration 121 retains private phase checkpoints and safely stops old batch runs. The protected migration job checks packaged hashes, applies it and verifies the schema. Rollback retains checkpoints and plan history. Notification, archive, Module 025 and protected deployment authority remain unchanged.
