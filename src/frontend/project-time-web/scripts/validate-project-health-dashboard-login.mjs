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
const authCardIndex = app.indexOf('<div className="auth-card phd-auth-card">');
const authStoryIndex = app.indexOf('<div className="auth-brand-block phd-auth-story">', authCardIndex);
const signedOutStart = app.indexOf('if (!authSession) {');
const signedOutEnd = app.indexOf("if (authSession?.loginMethod === 'local'", signedOutStart);
const signedOutExperience = signedOutStart >= 0 && signedOutEnd > signedOutStart
  ? app.slice(signedOutStart, signedOutEnd)
  : '';
const loginComponentsStart = app.indexOf('function SignalLogo()') >= 0
  ? app.indexOf('function SignalLogo()')
  : app.indexOf('function SignalLogo(');
const loginComponentsEnd = app.indexOf('function DataState(', loginComponentsStart);
const loginExperienceSource = loginComponentsStart >= 0 && loginComponentsEnd > loginComponentsStart
  ? `${app.slice(loginComponentsStart, loginComponentsEnd)}\n${signedOutExperience}`
  : '';

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
    && loginExperienceSource.includes('<SignalLogo productName="Pulse" ariaLabel="US Signal Pulse" />')
    && officialLogo.length === 31_117
    && crypto.createHash('sha256').update(officialLogo).digest('hex') === 'f28a48b72d16d5a2d0377d559ba0a549f4486309cc6e09a285a32840e0df806b'
);

requireInvariant(
  'PULSE_LOGIN_PLATFORM_BRANDING',
  containsAll(loginExperienceSource, [
    'aria-label="Pulse sign in"',
    'productName="Pulse"',
    'Unified Business Operations',
    'Every stage of delivery.',
    'One intelligent workspace.',
    'Welcome to Pulse',
    'Continue securely'
  ])
    && !loginExperienceSource.includes('Welcome to Project Health Dashboard')
    && !loginExperienceSource.includes('Time • Approval • Utilization')
);

requireInvariant(
  'PROJECT_HEALTH_DASHBOARD_PLATFORM_SCOPE',
  ['Sales', 'Opportunities', 'Projects', 'Time', 'Approvals', 'Billing', 'Invoicing', 'Analytics']
    .every((capability) => app.includes(`'${capability}'`))
);

requireInvariant(
  'PULSE_US_SIGNAL_MOTION_PASSWORD_ONLY',
  containsAll(loginExperienceSource, [
    "https://ussignal.com/wp-content/uploads/2025/01/Comp-33_4.gif",
    'function ProjectHealthDashboardLoginHero({ showSignalMotion = false })',
    '{!showSignalMotion ? (',
    "<ProjectHealthDashboardLoginHero showSignalMotion={loginRoute?.loginMethod === 'local'} />"
  ])
    && !loginExperienceSource.includes('phd-auth-motion-switcher')
    && !loginExperienceSource.includes('setMotionMode')
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

requireInvariant(
  'PROJECT_HEALTH_DASHBOARD_MOBILE_SIGN_IN_FIRST',
  authCardIndex >= 0
    && authStoryIndex > authCardIndex
    && containsAll(stylesheet, [
      '.phd-auth-story {',
      'grid-column: 1;',
      'grid-row: 2;',
      '.phd-auth-card {',
      'grid-row: 1;'
    ])
);

console.log(`PROJECT_HEALTH_DASHBOARD_LOGIN_VALIDATION=${failures.length === 0 ? 'PASSED' : 'FAILED'}`);
if (failures.length > 0) {
  console.error(`PROJECT_HEALTH_DASHBOARD_LOGIN_FAILURES=${failures.join(',')}`);
  process.exitCode = 1;
}
