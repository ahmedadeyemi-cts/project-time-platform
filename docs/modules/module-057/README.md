# Module 057 — Resource and Team Calendar Capacity

## Relationship to Module 010

Module 010 remains responsible for:

- Microsoft Entra SSO
- User import and synchronization
- Entra object IDs
- Email and identity mapping
- Departments, teams, and managers
- Test and production tenant configuration

Module 057 consumes Module 010 identity and organization data.

## Environments

- Test domain: `onenecklab.com`
- Production domain: `ussignal.com`

Domain behavior must be configuration-driven.

## Calendar selectors

Users may select:

- An individual engineer
- A team
- A department
- A date or date range
- A project
- Availability status

## Calendar views

Module 057 must support:

- Day
- Workweek
- Week
- Month
- Agenda
- Resource timeline
- Custom range

Month view must support direct month/year selection and navigation several
months into the future or past.

## Individual calendar

The individual view will show:

- Available
- Tentative
- Busy
- Working elsewhere
- Out of office
- Scheduled hours
- Available working hours
- Remaining capacity
- Meeting count

## Team calendar

The team view will show:

- One row or lane per engineer
- Combined free/busy calendar
- Scheduled hours
- Available hours
- Capacity percentage
- Overbooked warnings
- Underutilized indicators
- Daily, weekly, monthly, quarterly, and custom-range summaries

## Privacy

Team views show availability state by default.

Meeting subject, attendees, body, organizer, and location remain hidden unless
the signed-in role has explicit calendar-detail permission.

## Planned route

`#calendar-capacity`

## Planned API

- `GET /api/calendar/configuration`
- `GET /api/calendar/resources`
- `GET /api/calendar/users/{userId}/events`
- `POST /api/calendar/schedule`
- `GET /api/calendar/teams/{teamId}/capacity`
- `GET /api/calendar/departments/{departmentId}/capacity`
