import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const appPath = path.join(webRoot, 'src', 'App.jsx');
const stylesheetPath = path.join(webRoot, 'src', 'project-health-dashboard-login.css');
const officialLogoPath = path.join(webRoot, 'brand', 'USSNavyStacked.png');

const app = fs.readFileSync(appPath, 'utf8');
const stylesheet = fs.readFileSync(stylesheetPath, 'utf8');
const officialLogo = fs.readFileSync(officialLogoPath);
const failures = [];

function requireInvariant(name, condition) {
  console.log(`${name}=${condition ? 'PASSED' : 'FAILED'}`);
  if (!condition) failures.push(name);
}

function containsAll(source, values) {
  return values.every((value) => source.includes(value));
}

requireInvariant(
  'PROJECT_HEALTH_DASHBOARD_OFFICIAL_US_SIGNAL_LOGO',
  app.includes("import usSignalLogoUrl from '../brand/USSNavyStacked.png';")
    && app.includes('<img className="brand-logo-image" src={usSignalLogoUrl} alt="US Signal" />')
    && officialLogo.length === 31_117
    && crypto.createHash('sha256').update(officialLogo).digest('hex') === 'f28a48b72d16d5a2d0377d559ba0a549f4486309cc6e09a285a32840e0df806b'
);

requireInvariant(
  'PROJECT_HEALTH_DASHBOARD_PLATFORM_BRANDING',
  containsAll(app, [
    'Project Health Dashboard',
    'Unified Business Operations',
    'Every stage of delivery.',
    'One intelligent workspace.',
    'Welcome to Project Health Dashboard',
    'Continue securely'
  ])
    && !app.includes('Time • Approval • Utilization')
);

requireInvariant(
  'PROJECT_HEALTH_DASHBOARD_PLATFORM_SCOPE',
  ['Sales', 'Opportunities', 'Projects', 'Time', 'Approvals', 'Billing', 'Invoicing', 'Analytics']
    .every((capability) => app.includes(`'${capability}'`))
);

requireInvariant(
  'PROJECT_HEALTH_DASHBOARD_MOTION_OPTIONS',
  containsAll(app, [
    "https://ussignal.com/wp-content/uploads/2025/01/Comp-33_4.gif",
    'Platform ecosystem',
    'US Signal motion',
    'ProjectHealthDashboardLoginHero'
  ])
);

requireInvariant(
  'PROJECT_HEALTH_DASHBOARD_SCOPED_RESPONSIVE_STYLES',
  app.includes("import './project-health-dashboard-login.css';")
    && containsAll(stylesheet, [
      '.phd-auth-shell',
      '.phd-auth-experience',
      '.phd-platform-motion',
      '@media (max-width: 820px)',
      '@media (prefers-reduced-motion: reduce)'
    ])
    && !/(^|\n)\s*(?:html|body|:root|#root|main|button|input)\s*[{,]/m.test(stylesheet)
);

console.log(`PROJECT_HEALTH_DASHBOARD_LOGIN_VALIDATION=${failures.length === 0 ? 'PASSED' : 'FAILED'}`);
if (failures.length > 0) {
  console.error(`PROJECT_HEALTH_DASHBOARD_LOGIN_FAILURES=${failures.join(',')}`);
  process.exitCode = 1;
}
