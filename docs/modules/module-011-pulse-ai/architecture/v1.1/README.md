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

## Package contents

| File | Purpose | SHA-256 |
|---|---|---|
| `US_Signal_Pulse_AI_Private_Intelligence_Architecture_v1.1.docx` | Editable architecture document | `938a3e98eeac190c8ab465838e8d418608861bab92787828b33a137eb2569b76` |
| `US_Signal_Pulse_AI_Private_Intelligence_Architecture_v1.1.pdf` | Published architecture document | `91d04496e79ae7e79b2391c1142f0803295eda8b6490308888132da066446680` |
| `US_Signal_Pulse_AI_Architecture_Diagrams_v1.1.pdf` | Combined systems-engineering diagram package | `0a7fa63c4b1e1234fae031781d429bc7dfe71a3a0e5cd4c25407dc253dcd209e` |
| `US_Signal_Pulse_AI_Logical_Architecture_v1.1.png` | High-resolution private-first logical architecture | `b0476094bd4035829c001274302ffb09fe87c73b59b3c45c29497c92d265b292` |
| `US_Signal_Pulse_AI_Logical_Architecture_v1.1.svg` | Editable vector private-first logical architecture | `3b35d75d3d091941ae74ac7604ff511661b8156b370795166652500b01dd36df` |
| `US_Signal_Pulse_AI_Deployment_Network_Architecture_v1.1.png` | High-resolution deployment and network architecture | `34cb8dfd0afbfcf1ebc5c3bec9b2ce94054ce3efdbdb640c179ab0555695a156` |
| `US_Signal_Pulse_AI_Deployment_Network_Architecture_v1.1.svg` | Editable vector deployment and network architecture | `6ec0560358602004a042e403278ef3b5bbefe41b5f75d54d145484e87fc81f95` |
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
