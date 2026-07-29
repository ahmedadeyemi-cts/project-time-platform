# US Signal Pulse AI Architecture Package — Version 1.1

**Owner:** US Signal  
**Product:** Pulse  
**Module:** 011 — Pulse AI  
**Classification:** US Signal Internal — Confidential  
**Architecture status:** Review baseline  
**Version:** 1.1  
**Published to repository:** 2026-07-29

## Purpose

This directory contains the approved US Signal-branded architecture package for the private-first Pulse AI design. The package explains how Pulse authenticates and authorizes a user, retrieves internal documents and governed live data, invokes a private Pulse AI model, measures confidence, optionally prepares a sanitized reasoning capsule for Module 064 and an approved external LLM, verifies the result privately, and returns a detailed cited answer or reviewable draft.

The package uses **Pulse** as the application name and **US Signal** as the document owner. Version 1.1 is the canonical repository baseline for presentation, architecture review, and systems-engineering discussion.

The repository copies are losslessly packaged or publication-optimized for source-control distribution. Their layout and content were rendered and visually verified before publication.

## Package contents

| File | Purpose | SHA-256 |
|---|---|---|
| `US_Signal_Pulse_AI_Private_Intelligence_Architecture_v1.1.docx` | Editable architecture document | `37bad4b993560ca16d82470279b151dd270993d5d786209ac2bae238fd4a3a7f` |
| `US_Signal_Pulse_AI_Private_Intelligence_Architecture_v1.1.pdf` | Published architecture document | `aeb005a19d67541047fc013ecfefedd6b3c3ceaffdd59214a82ce800df4f6f8e` |
| `US_Signal_Pulse_AI_Architecture_Diagrams_v1.1.pdf` | Combined systems-engineering diagram package | `21dff6e34ed3d83657963ddba5a05ca16acefa333d7d21bbbbd1207de3351b4b` |
| `US_Signal_Pulse_AI_Logical_Architecture_v1.1.png` | High-resolution private-first logical architecture | `22180ea7d3890407756d33e95642f94ffdb8f751bb05ac25f31c6761759be5fc` |
| `US_Signal_Pulse_AI_Logical_Architecture_v1.1.svg` | Editable vector private-first logical architecture | `dcc3c2148db944eb02c55ee073f83990afe3212080931d1c7b4e8013854e0996` |
| `US_Signal_Pulse_AI_Deployment_Network_Architecture_v1.1.png` | High-resolution deployment and network architecture | `2a51f56e89d4abfd222f734c3c4e0cb1f0fd75830caa1a094ed34f736275dcb4` |
| `US_Signal_Pulse_AI_Deployment_Network_Architecture_v1.1.svg` | Editable vector deployment and network architecture | `c5d8f4f0511fe001103c04a62cba741fb18adde75d3857cfe363b7572a62aded` |
| `assets/US_Signal_Logo.jpg` | Source US Signal logo supplied for this architecture package | `c4fc4b33f744d065deeec531f393aa39996273e51eb946a452b1319e6e529183` |

## Architecture summary

```text
Pulse
  |
Authentication, roles, permissions, and record scope
  |
  +-- Private document retrieval: SOW, GSD, architecture and design documents
  |
  +-- Governed live-data tools: projects, time, reports and financials
  |
Private Pulse AI model
  |
Confidence assessment
  |
  +-- Sufficient evidence: continue privately
  |
  +-- Generic help required: Sanitization/DLP gateway -> Module 064 -> approved Claude or OpenAI route
  |
Private evidence verification
  |
Detailed cited answer or reviewable draft
```

## Security boundary

- Raw SOW, GSD, customer, contract, architecture, project, financial, and employee information remains inside the approved private Pulse boundary by default.
- The private Pulse AI model is the primary reasoning engine for restricted information.
- Claude or OpenAI is optional and may receive only a policy-approved sanitized reasoning capsule.
- Module 064 remains the governed provider, secret, health, usage, circuit-breaker, and routing boundary.
- All retrieval and tool execution follows the effective user's role, module, project, customer, record, and field scope.
- External reasoning must be verified privately before it is presented as an answer or draft.
- The architecture does not authorize autonomous timesheet submission, FlowHive baseline approval, financial mutation, model training, provider activation, or production deployment.

## Repository governance

This directory stores architecture artifacts only. It contains no credentials, connection strings, customer document contents, model weights, embeddings, deployment scripts, database migration, Azure operation, provider activation, or production-changing configuration.
