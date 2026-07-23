import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(process.cwd(), '../../..');

async function text(path) {
  return readFile(resolve(root, path), 'utf8');
}

function requireText(source, values, label) {
  for (const value of values) {
    if (!source.includes(value)) {
      throw new Error(`${label} is missing required contract: ${value}`);
    }
  }
}

const paths = {
  main: 'src/frontend/project-time-web/src/main.jsx',
  portal: 'src/frontend/project-time-web/src/DashboardPersonalCalendarPortal.jsx',
  css: 'src/frontend/project-time-web/src/dashboard-personal-calendar.css',
  context: 'src/frontend/project-time-web/src/PageContextGuide.jsx',
  calendarBackend: 'src/backend/ProjectTime.Api/Modules/CalendarCapacityModule.cs',
  approvalBackend: 'src/backend/ProjectTime.Api/Modules/ApprovalCenterModule.cs',
  packageJson: 'src/frontend/project-time-web/package.json'
};

const [main, portal, css, context, calendarBackend, approvalBackend, packageJson] = await Promise.all(
  Object.values(paths).map(text)
);

requireText(main, [
  "import DashboardPersonalCalendarPortal from './DashboardPersonalCalendarPortal.jsx';",
  '<DashboardPersonalCalendarPortal />',
  '<App />'
], 'Application root integration');

if (main.indexOf('<DashboardPersonalCalendarPortal />') < main.indexOf('<App />')) {
  throw new Error('The dashboard calendar portal must render after App creates the role-aware dashboard host.');
}

requireText(portal, [
  "const DASHBOARD_ROUTE = 'dashboard';",
  "const HOST_ID = 'dashboard-personal-calendar-host';",
  "document.querySelector('#role-welcome-dashboard')",
  "api(`/api/calendar/resources?dashboardScope=${Date.now()}`)",
  "api('/api/calendar/schedule'",
  'resourceIds: [currentUserId]',
  "teamName: ''",
  "departmentName: ''",
  "view: 'thisweek'",
  'Array.from({ length: 5 }',
  'Your personal Monday–Friday calendar for the current effective user.',
  'Open capacity center',
  "weekStart: startOfApprovalWeek()",
  "includeAll: 'false'",
  "allDates: 'false'",
  "label.textContent = 'Current week approvals'",
  "row.dataset.dashboardApprovalScope = 'current-week'",
  "window.addEventListener('projectpulse:view-as-changed', refreshDashboard)",
  "window.addEventListener('projectpulse:approval-queue-changed', refreshDashboard)"
], 'Dashboard personal calendar and current-week approval alignment');

for (const forbidden of [
  "scope === 'team'",
  "scope === 'department'",
  'resourceIds: resources.map',
  'resourceIds: selected'
]) {
  if (portal.includes(forbidden)) {
    throw new Error(`The dashboard calendar must remain individual-only: ${forbidden}`);
  }
}

requireText(css, [
  '#dashboard-personal-calendar-host',
  '.dashboard-personal-calendar',
  '.dashboard-personal-calendar-board',
  'grid-template-columns: minmax(15.5rem, 1.1fr) minmax(0, 5fr);',
  '.dashboard-personal-calendar-days',
  'grid-template-columns: repeat(5, minmax(9rem, 1fr));',
  '.dashboard-calendar-event'
], 'Dashboard calendar visual contract');

requireText(context, [
  '<aside className="page-context-guide" aria-label="Page context guide">',
  '<details>',
  '<summary>',
  'Purpose',
  'Backend support',
  'What to check'
], 'Collapsible page context guide');

if (/<details\s+open(?:=|\s|>)/.test(context)) {
  throw new Error('The page context guide must be collapsed by default on every page.');
}

requireText(calendarBackend, [
  'app.MapGet("/api/calendar/resources"',
  'currentUserId = actor.Value',
  'app.MapPost("/api/calendar/schedule"',
  'request.ResourceIds',
  'scheduleItems = items'
], 'Existing calendar API authority');

requireText(approvalBackend, [
  'app.MapGet("/api/manager/approvals"',
  "tds.status = 'submitted'",
  '@date_from',
  '@date_to'
], 'Current-week approval API authority');

requireText(packageJson, [
  'validate:dashboard-personal-calendar',
  'node ./scripts/validate-dashboard-personal-calendar-context.mjs',
  'npm run validate:dashboard-personal-calendar'
], 'Production build wiring');

console.log('Individual dashboard calendar, current-week approval alignment, and default-collapsed page context contracts passed.');
