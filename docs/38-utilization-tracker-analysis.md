# Utilization Tracker Spreadsheet Analysis

## Purpose

This document captures the utilization calculation model observed from the uploaded 2026 utilization tracker workbook.

## Workbook Structure

The workbook contains four quarterly tabs:

```text
2026 Q1
2026 Q2
2026 Q3
2026 Q4
```

Each tab tracks weekly utilization for the quarter.

## Weekly Structure

Each quarter tab includes week-ending Saturday rows.

Observed weekly columns:

```text
Week Ending Saturday
Utilized Hrs
OT Utilized Hrs
PTO Hrs
PreSales Hrs or PreSales/Train
Total
Utilized % w/o PreSales
Total Utilized %
```

## Core Utilization Formula

Observed total formula:

```text
Total = Utilized Hrs + OT Utilized Hrs + PTO Hrs + PreSales/Train Hrs
```

Observed utilization percentage without presales:

```text
Utilized % w/o PreSales = (Total - PreSales/Train Hrs) / Standard Quarterly Hours
```

Observed total utilization percentage:

```text
Total Utilized % = Total / Standard Quarterly Hours
```

## Standard Quarterly Hours

The spreadsheet uses:

```text
482 hours
```

as the standard quarterly denominator.

## Target Thresholds

The workbook includes target thresholds from 70% through 105%:

```text
70%
75%
80%
85%
90%
95%
100%
105%
```

The hours-needed calculation uses:

```text
Hours Needed = (Standard Quarterly Hours * Target Percent) - Current Quarterly Total
```

## Bonus Reference Values

The workbook includes example bonus/reference values tied to utilization targets:

```text
70%  -> 6240
75%  -> 6630
80%  -> 7800
85%  -> 8190
90%  -> 8580
95%  -> 8970
100% -> 9360
105% -> 9750
```

These should be treated as configurable policy values rather than hardcoded values.

## PreSales and Training Rule

The workbook notes:

```text
PreSales only counts if approved
```

The application should therefore track presales/training time separately and only include it in utilization when the relevant approval condition is met.

## PTO Rule

The workbook includes PTO as part of the utilization total.

This confirms that selected non-project time categories, especially vacation or paid time off categories, may count toward utilization depending on policy.

## Implementation Direction

The system should support:

```text
weekly utilization summaries
quarterly utilization policies
standard quarterly denominator hours
configurable target thresholds
configurable bonus/reference values
separate regular utilized hours
separate afterhours/OT utilized hours
separate PTO hours
separate PreSales/Training hours
approval-controlled PreSales/Training inclusion
```

## Data Model Direction

Recommended database additions:

```text
utilization_policies
utilization_policy_targets
utilization_weekly_summaries
project_tasks.utilization_bucket
non_project_time_categories.utilization_bucket
additional utilization_snapshots detail columns
```

## Open Items

The exact payroll or bonus calculation logic should be confirmed later. For now, bonus/reference values will be stored as policy metadata and not treated as final payroll logic.
