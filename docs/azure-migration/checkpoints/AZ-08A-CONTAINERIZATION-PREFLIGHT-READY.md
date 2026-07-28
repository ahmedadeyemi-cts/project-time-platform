# AZ-08A — Containerization Preflight Ready

Date: 2026-07-12

## Source baseline

- branch: `source/work-register-billing-lifecycle-20260712`
- expected commit: `9cf36c2ab28c5eb00bd379bd63b2c8e07cd3af84`
- application PR: #11

## Purpose

`deployment/azure/scripts/az08a-containerization-preflight-readonly.sh` performs a read-only source-host inspection before API and frontend container definitions are added.

It verifies:

- the active source branch and exact reviewed commit
- clean working-tree status
- available OCI/container tooling
- .NET, Node, npm, and Python versions
- required backend and frontend build inputs
- existing Dockerfile or Containerfile inventory
- backend target framework and health endpoint
- configuration key names without reading or printing values
- local frontend proxy behavior and expected ports

## Safety

AZ-08A:

- does not modify source files
- does not stage, commit, fetch, merge, or push Git changes
- does not build or push images
- does not create Azure resources
- does not print environment variable values or secret material

The image build remains blocked until container definitions are designed and validated after this preflight.
