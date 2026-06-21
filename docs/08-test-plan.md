# Test Plan

## 1. Purpose

This document defines the initial testing approach for the platform.

Testing must prove that core workflows continue to work after code changes, database migrations, OS upgrades, database upgrades, and dependency updates.

## 2. Test Categories

| Test Type | Purpose |
|---|---|
| Unit Tests | Validate individual business rules and services |
| Integration Tests | Validate API, database, and authentication interactions |
| UI Tests | Validate frontend behavior |
| Workflow Tests | Validate end-to-end business processes |
| Upgrade Tests | Validate system after OS/DB/application upgrades |
| Security Tests | Validate role and scope restrictions |

## 3. Critical Workflow Tests

The following workflows must be tested repeatedly:

1. Engineer logs in.
2. Engineer enters time.
3. Engineer submits time.
4. Manager approves time.
5. Manager declines time.
6. Engineer corrects declined time.
7. Project Manager approves project/task time.
8. Project Manager declines project/task time.
9. Accounting reconciles approved time.
10. Accounting locks a period.
11. Utilization is calculated.
12. Monthly utilization email is generated.
13. Quarterly utilization email is generated.
14. Audit records are created.

## 4. Role Security Tests

| Scenario | Expected Result |
|---|---|
| Engineer tries to approve time | Denied |
| Team Lead tries to approve time | Denied unless separately assigned Manager/PM role |
| Manager views non-direct report | Denied unless broader role exists |
| PM views unassigned project | Denied unless broader role exists |
| Accounting edits user roles | Denied unless also admin |
| Organizational Admin views org-wide data | Allowed |
| System Admin changes identity settings | Allowed |

## 5. Utilization Tests

The system must validate:

- 70% target calculation.
- Hours needed to reach 70%.
- Next 5% increment calculation.
- Approved vs submitted vs reconciled hour calculations.
- Quarterly date boundaries.
- Exclusion of PTO/holiday/non-utilization hours if configured.

## 6. Upgrade Validation Checklist

After each upgrade, validate:

- Application loads.
- Login works.
- Database connection works.
- Time entry works.
- Approval workflows work.
- Reports load.
- Notifications can be generated.
- Audit events are written.

## 7. Test Log

| Date | Test Type | Environment | Result | Notes |
|---|---|---|---|---|
| TBD | TBD | TBD | TBD | TBD |
