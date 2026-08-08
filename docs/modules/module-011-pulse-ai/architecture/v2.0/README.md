# US Signal Celar AI Architecture Package — Version 2.0

**Owner:** US Signal  
**Platform:** Pulse  
**Module:** 011 — Celar AI target brand  
**Classification:** US Signal Internal — Confidential  
**Architecture status:** Documentation-first rebrand baseline  
**Published:** July 30, 2026  
**OpenCloud topology addendum:** August 8, 2026

## Purpose

This directory contains the US Signal-branded Celar AI architecture package. It updates the Module 011 architecture and diagrams before any runtime rebrand and preserves Version 1.1 as the historical Pulse AI baseline.

Version 2.0 adds:

- the Celar AI core identity;
- Dr. Ahmed Adeyemi creator and engineering-direction attribution;
- the Celeritas and speed-of-light name origin;
- the US Signal fiber and digital-infrastructure connection;
- the Professional Services speed-of-delivery mission;
- the Changepoint business catalyst;
- the canonical answer to “What is Celar AI?”;
- updated logical and deployment/network diagrams; and
- an OpenCloud private-runtime diagram showing Ollama, Tesseract, and ClamAV as isolated containers on one shared Test/UAT Linux VM;
- the boundary that keeps Pulse application and PostgreSQL placement independent from that shared runtime VM;
- a production evolution path that can move Ollama to GPU-capable compute while retaining Tesseract and ClamAV on CPU compute; and
- a documented brand-clearance boundary.

## Current cost-control decision

The additional Azure private-runtime deployment is deferred until OpenCloud is available. No placeholder endpoints or tokens should be configured. Structured Celar AI questions backed by authorized Pulse databases and APIs remain independent of the private document runtime.

FlowHive continues to fail closed when citation-ready SOW evidence is unavailable. It may be enabled only after the OpenCloud runtime passes live malware scan, extraction/OCR, retrieval, approval, citation, and end-to-end plan-generation checks.

## OpenCloud Test/UAT placement

The approved starting topology uses one private Linux VM with separate OCI/Podman containers for:

- Ollama private inference and embeddings;
- the Tesseract OCR adapter; and
- ClamAV malware scanning.

Persistent model, signature, work, and configuration volumes remain separate. The shared VM does not need to host the Pulse web/API application or PostgreSQL.

## Canonical target-brand package

```text
docs/modules/module-011-pulse-ai/architecture/v2.0/
```

## Historical architecture package

```text
docs/modules/module-011-pulse-ai/architecture/v1.1/
```

## Transition boundary

The business platform remains Pulse. This package changes documentation and diagrams only. Runtime labels, routes, APIs, source directories, database objects, permission codes, feature codes, environment variables, Module 064 configuration, models, credentials, Test resources, and Production resources remain unchanged.

## Brand-governance note

`Celar` is already used by other organizations. External use requires US Signal Legal and Marketing name-clearance, trademark, pronunciation, domain, and digital-identity review.
