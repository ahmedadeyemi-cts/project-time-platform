import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const appPath = path.join(webRoot, 'src', 'App.jsx');
const authenticatedHelpAssistantPath = path.join(webRoot, 'src', 'AuthenticatedHelpAssistant.jsx');
const mainPath = path.join(webRoot, 'src', 'main.jsx');
const stylesheetPath = path.join(webRoot, 'src', 'pulse-login.css');
const officialLogoPath = path.join(webRoot, 'brand', 'USSNavyStacked.png');
const brandedMotionPath = path.join(webRoot, 'brand', 'pulse-secure-access.gif');

const app = fs.readFileSync(appPath, 'utf8');
const authenticatedHelpAssistant = fs.readFileSync(authenticatedHelpAssistantPath, 'utf8');
const main = fs.readFileSync(mainPath, 'utf8');
const stylesheet = fs.readFileSync(stylesheetPath, 'utf8');
const officialLogo = fs.readFileSync(officialLogoPath);
const brandedMotion = fs.readFileSync(brandedMotionPath);
const failures = [];
const authCardIndex = app.indexOf('<div className="auth-card pulse-auth-card">');
const authStoryIndex = app.indexOf('<div className="auth-brand-block pulse-auth-story">', authCardIndex);
const signedOutStart = app.indexOf('if (!authSession) {');
const signedOutEnd = app.indexOf("if (authSession?.loginMethod === 'local'", signedOutStart);
const signedOutExperience = signedOutStart >= 0 && signedOutEnd > signedOutStart
  ? app.slice(signedOutStart, signedOutEnd)
  : '';
const secureMotionImageStart = stylesheet.indexOf('.pulse-secure-motion img {');
const secureMotionImageEnd = stylesheet.indexOf('}', secureMotionImageStart);
const secureMotionImageStyles = secureMotionImageStart >= 0 && secureMotionImageEnd > secureMotionImageStart
  ? stylesheet.slice(secureMotionImageStart, secureMotionImageEnd)
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
  'PULSE_OFFICIAL_US_SIGNAL_LOGO',
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
    && !loginExperienceSource.includes('Time • Approval • Utilization')
);

requireInvariant(
  'PULSE_PLATFORM_SCOPE',
  ['Sales', 'Opportunities', 'Projects', 'Time', 'Approvals', 'Billing', 'Invoicing', 'Analytics']
    .every((capability) => app.includes(`'${capability}'`))
);

requireInvariant(
  'PULSE_BRANDED_MOTION_PASSWORD_ONLY',
  app.includes("import pulseSecureAccessMotionUrl from '../brand/pulse-secure-access.gif';")
    && containsAll(loginExperienceSource, [
    'function PulseLoginHero({ showSecureMotion = false })',
    '{!showSecureMotion ? (',
    'alt="Pulse animated secure operational network"',
    "<PulseLoginHero showSecureMotion={loginRoute?.loginMethod === 'local'} />"
  ])
    && brandedMotion.subarray(0, 6).toString('ascii') === 'GIF89a'
    && brandedMotion.length === 223_290
    && crypto.createHash('sha256').update(brandedMotion).digest('hex') === '001fe1a6a17c49afcef0639cd765d862026ea043b8fea0f0af2feb89d52b7914'
    && secureMotionImageStyles.includes('object-fit: contain;')
    && !secureMotionImageStyles.includes('object-fit: cover;')
    && !loginExperienceSource.includes('ussignal.com/wp-content/uploads')
    && !loginExperienceSource.includes('pulse-auth-motion-switcher')
    && !loginExperienceSource.includes('setMotionMode')
);

requireInvariant(
  'PULSE_CELAR_AI_AUTHENTICATED_ONLY',
  !signedOutExperience.includes('pulse-celar-note')
    && !signedOutExperience.includes('Your intelligent assistant is ready after sign-in.')
    && containsAll(authenticatedHelpAssistant, [
      "const AUTH_SESSION_STORAGE_KEY = 'projectPulseAuthSession';",
      'function hasUsableAuthSession()',
      "window.addEventListener('projectpulse:auth-session-ready', refreshAuthVisibility);",
      "window.addEventListener('projectpulse:auth-session-cleared', refreshAuthVisibility);",
      'return hasAuthenticatedSession ? <HelpAssistant /> : null;'
    ])
    && main.includes("import AuthenticatedHelpAssistant from './AuthenticatedHelpAssistant.jsx';")
    && main.includes('<AuthenticatedHelpAssistant />')
    && !main.includes('<HelpAssistant />')
    && app.includes("window.dispatchEvent(new CustomEvent('projectpulse:auth-session-cleared'));")
);

requireInvariant(
  'PULSE_SCOPED_RESPONSIVE_STYLES',
  app.includes("import './pulse-login.css';")
    && containsAll(stylesheet, [
      '.pulse-auth-shell',
      '.pulse-auth-experience',
      '.pulse-platform-motion',
      '@media (max-width: 820px)',
      '@media (prefers-reduced-motion: reduce)'
    ])
    && !/(^|\n)\s*(?:html|body|:root|#root|main|button|input)\s*[{,]/m.test(stylesheet)
);

requireInvariant(
  'PULSE_MOBILE_SIGN_IN_FIRST',
  authCardIndex >= 0
    && authStoryIndex > authCardIndex
    && containsAll(stylesheet, [
      '.pulse-auth-story {',
      'grid-column: 1;',
      'grid-row: 2;',
      '.pulse-auth-card {',
      'grid-row: 1;'
    ])
);

console.log(`PULSE_LOGIN_VALIDATION=${failures.length === 0 ? 'PASSED' : 'FAILED'}`);
if (failures.length > 0) {
  console.error(`PULSE_LOGIN_FAILURES=${failures.join(',')}`);
  process.exitCode = 1;
}
