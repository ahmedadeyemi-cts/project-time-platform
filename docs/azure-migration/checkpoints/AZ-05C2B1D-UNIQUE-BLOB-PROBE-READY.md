# AZ-05C2B1D — Unique Blob Probe Ready

**Date:** 2026-07-12

## Purpose

Avoid stale Azure Managed Run Command instance-view output when retesting Blob DNS and access.

## Canonical submitter

`deployment/azure/scripts/az05c2b1d-submit-unique-blob-access-probe.sh`

The submitter creates a newly named managed Run Command for every probe execution and then displays that exact command's instance view.

## Safety

The probe is read-only and does not:

- stop or update the active PostgreSQL restore command;
- modify PostgreSQL;
- modify storage data;
- change RBAC;
- restart or deallocate the VM.

## Required order

1. Complete `AZ-05C2B1C2` DNS zone-group and East VNet-link repair.
2. Run the unique Blob probe.
3. Confirm Blob DNS resolves privately and Blob listing succeeds.
4. Evaluate whether the original AzCopy process resumes before taking any action against the restore attempt.
