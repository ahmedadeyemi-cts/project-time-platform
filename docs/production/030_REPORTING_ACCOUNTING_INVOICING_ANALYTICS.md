# 030 Reporting / Accounting / Invoicing / Analytics Command Center

## Status
Applied as complete Module 030 pending validation and commit.

## 030A Reporting Criteria Builder
Adds criteria for report type, date basis, daily, weekly, monthly, custom date range, year-to-date, fiscal period, customer, project, PM, engineer, selected engineers, team, organization, contract type, time status, approval status, invoice status, work code, work location, external connection, API area, authentication event, system component, grouping, and export format.

## 030B Time Entry Reporting
Covers all time entered, submitted, approved, rejected, returned, missing, late, billable, non-billable, utilization, AI-assisted entries, and SOW alignment.

## 030C Accounting / Invoicing Reporting
Covers invoice-ready time, non-invoiced time, invoiced amount, rate, hours, PO/quote, CP invoice number, contract type, work code, work location, total invoice amount, credits, and exceptions.

## 030D Customer Reporting
Covers customer, engagement, project, contract type, invoice status, billable hours, total invoiced amount, billing exposure, and exceptions.

## 030E Project Reporting
Covers project status, PM, customer, SOW/GSD, signed handoff, assignment, workload, time entered, and billing readiness.

## 030F PM Reporting
Covers PM assigned projects, PM workload, PM validation backlog, returned/rejected time, project billing readiness, and handoff gaps.

## 030G Engineer Reporting
Covers engineer, selected engineers, utilization, submitted time, missing time, billable/non-billable, allocation, AI-assisted entries, and SOW alignment.

## 030H Team / Organization Reporting
Covers team, department, organization-wide utilization, billable/non-billable totals, workload distribution, and approval backlog.

## 030I Workflow / Approval / Audit Reporting
Covers approval backlog, View-As audit, export readiness, handoff audit, assignment audit, notification audit, and UAT evidence audit.

## 030J System Stability Reporting
Covers frontend, API, database, nginx, service status, validation checks, uptime placeholders, and production readiness indicators.

## 030K API Status Reporting
Covers authentication, navigation, dashboard, notification, email provider, recipient safety, readiness, CRM, SOW, and AI provider APIs.

## 030L External Connection Reporting
Covers CRM, Salesforce, Zendesk Sell, Claude, Azure, Brevo, SSO/Auth, recipient safety, and future integrations.

## 030M Authentication / Security Reporting
Covers SSO login activity, session_required events, role access checks, View-As activity, forbidden writes, and admin/system access readiness.

## 030N Export Center
Adds export-ready layout preview for invoice, accounting, customer, PM, engineer, project, time entry, system health, API, external connection, and authentication reports.

## 030O Executive Summary Dashboard
Adds KPI summary backed by detailed drill-down report criteria and preview rows.

## 030P Report Library
Adds saved report definition model with name, type, owner, cadence, audience, criteria, selected columns, and export format.

## 030Q Closeout
Adds readiness checklist covering criteria builder, time reporting, accounting/invoicing, customer/project/PM/engineer, team/organization, system/API/external/authentication, export center, role visibility, and closeout.

## Sample Invoice Schema
The accounting invoice report supports these fields:

- Engagement Manager
- Customer
- Engagement
- Contract Type
- PO / Quote
- Invoicing Instructions
- CP Invoice Number
- Invoice Date
- Category
- Item Description
- Quantity / Hours Entered
- Rate
- Amount
- Work Code
- Work Location
- Total Invoiced Amount
