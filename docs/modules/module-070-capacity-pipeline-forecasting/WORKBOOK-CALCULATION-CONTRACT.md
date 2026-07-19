# Workbook Calculation Contract

## Audited source

`PS Capacity Planning(1).xlsx`, supplied for Module 070 design on 2026-07-19.
The workbook contains Collaboration, Systems, and Networking sheets. It is an
input reference only; engineer names are not copied into source code or a second
identity store.

## Workbook formula verified

Across sampled weekly columns and sheet totals, the workbook computes:

```text
revised demand = current planned hours + future project hours - supplemental/LTE hours
```

Displayed sample formulas and totals reconcile with this expression. The
workbook does not calculate total available team capacity, remaining capacity,
or utilization, so it cannot independently show whether revised demand exceeds
the team supply.

## Module 070 formulas

```text
unfilled pipeline = max(requested hours - confirmed/active allocated hours, 0)
weighted pipeline = unfilled pipeline × request-status probability weight
net demand = max(committed demand + weighted pipeline - supplemental capacity, 0)
remaining capacity = available capacity - net demand
utilization % = net demand / available capacity × 100
```

When available capacity is zero, utilization is `null` instead of dividing by
zero. Net demand cannot become negative when supplemental scenario capacity is
larger than demand. Negative remaining capacity is explicitly marked as over capacity.
Weighted request hours are divided evenly across the continuous forecast weeks
that overlap the request's start/end dates.

## Probability policy

| Request state | Weight |
|---|---:|
| Approved, assigned, confirmed, in progress | 100% |
| Submitted, PM/manager/coordinator review, requested | 60% |
| Draft, proposed | 25% |
| Other open state | 50% |

This policy is returned by the model endpoint and must be reviewed before
activation. Safety refusals or AI routing are not part of this calculation.

## Data-quality findings and controls

- One workbook demand cell contained the nonnumeric marker `x`; spreadsheet
  totals silently ignored it. Module 070 accepts numeric scenario hours only.
- Workbook dates jump from 2024 to 2026. Module 070 generates every intervening
  Monday for the selected horizon.
- Workbook names are free text. Module 070 uses stable identity IDs and a live
  dropdown so renamed people do not break references.
- Workbook supplemental/LTE rows have no canonical ProjectPulse database tag.
  The module treats the value as a visible, non-persistent scenario input.
- Opportunities contain financial pipeline signals but no governed labor-hour
  estimate; Module 070 never infers hours from opportunity revenue.
