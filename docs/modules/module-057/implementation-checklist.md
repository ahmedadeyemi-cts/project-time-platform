# Module 057 implementation checklist

## Module 010 SSO repair

- Replace hardcoded `@ussignal.com` login routing.
- Permit `onenecklab.com` in test.
- Preserve `ussignal.com` for production.
- Preserve approved local administrator domains.
- Use configured allowed-domain values in both login route and callback.
- Validate Entra tenant, client ID, redirect URI, and Graph scopes.

## Calendar API

- Implement privacy-safe Microsoft Graph free/busy lookup.
- Implement authorized individual calendar-event retrieval.
- Add team and department aggregation.
- Add future date-range and pagination controls.
- Add role and team scope enforcement.

## Calendar interface

- Individual, team, and department selectors.
- Day, workweek, week, month, agenda, and timeline views.
- Direct month/year selector.
- Previous/next period controls.
- Multi-month future navigation.
- Custom date range.
- Capacity summaries.
- Privacy-safe event display.

## Validation

- OneNeckLab test SSO.
- Individual calendar.
- Team free/busy.
- Month navigation.
- Future-month navigation.
- Role enforcement.
- Privacy enforcement.
