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
| `US_Signal_Pulse_AI_Private_Intelligence_Architecture_v1.1.docx` | Editable architecture document | `297de76633e63616d40ecd53232e4ae122b8f6c02a1a5e0cb040f8c0d9b5acbb` |
| `US_Signal_Pulse_AI_Private_Intelligence_Architecture_v1.1.pdf` | Published architecture document | `5ec2776d74da1c399dfb7b52b50a827d061038cdcc198a2e6dd31ce224c45d63` |
| `US_Signal_Pulse_AI_Architecture_Diagrams_v1.1.pdf` | Combined systems-engineering diagram package | `510ec8d789b6c068513a26a81afa041ee0e1cdaa7fcb46dd00bc1a2991257ebe` |
| `US_Signal_Pulse_AI_Logical_Architecture_v1.1.png` | High-resolution private-first logical architecture | `2e07e9b8a30685be9f365e422727f8c0d1a7c38f94c975c962c8bea57296d7cb` |
| `US_Signal_Pulse_AI_Logical_Architecture_v1.1.svg` | Editable vector private-first logical architecture | `a7adbe4e4b0f40f34c06a1a497c9c192d5e53708fdc3919c0fc902a963b64b41` |
| `US_Signal_Pulse_AI_Deployment_Network_Architecture_v1.1.png` | High-resolution deployment and network architecture | `a62ffc29629b1e7a7c1d9a30aa5afc5a0d889461ebdf98f14ff051c11432183c` |
| `US_Signal_Pulse_AI_Deployment_Network_Architecture_v1.1.svg` | Editable vector deployment and network architecture | `8ff860932d94dd91ad9e2e7e158cccb3e6927f8738fc50546a8aa5a2db0c795b` |
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
