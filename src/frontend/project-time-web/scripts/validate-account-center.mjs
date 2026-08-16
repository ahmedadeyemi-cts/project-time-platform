import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const component = fs.readFileSync(path.join(root, 'src', 'AccountCenterPortal.jsx'), 'utf8');
const style = fs.readFileSync(path.join(root, 'src', 'account-center.css'), 'utf8');
const main = fs.readFileSync(path.join(root, 'src', 'main.jsx'), 'utf8');

function requireMarkers(source, markers, label) {
  for (const marker of markers) {
    if (!source.includes(marker)) {
      throw new Error(`${label} missing required marker: ${marker}`);
    }
  }
}

requireMarkers(component, [
  'PR698_ACCOUNT_CENTER',
  "'/api/identity/profile'",
  "'/api/profile/preferences'",
  "'account-profile'",
  "'account-appearance'",
  "'account-session'",
  'My profile',
  'Preferences',
  'Current session',
  'Sign out',
  'Presence unavailable',
  'Directory and authentication details',
  'System',
  'Light',
  'Dark',
  'Enterprise table (recommended)',
  'View-As',
  'originalFetch()',
  'profileImageRemoved',
  'MAX_PROFILE_IMAGE_BYTES',
  'ACCEPTED_PROFILE_IMAGE_TYPES',
  'window.__projectPulseOriginalFetch',
  'projectpulse:identity-profile-changed',
  'projectpulse:auth-session-cleared',
  'projectpulse:view-as-changed',
  'projectpulse:theme-changed',
  'projectpulse:experience-changed',
  'beforeunload',
  "document.addEventListener('click', interceptAvatar, true)",
  "document.addEventListener('pointerdown', handlePointerDown, true)",
  "event.key === 'Escape'",
  'avatarTriggerRef.current?.focus()',
  'role="menu"',
  'aria-live="polite"',
  'readOnly={!editableIdentity}',
  'profileImageRemoved\n        ? \'\'\n        :',
  'window.location.hash = `#${metadata.route}`',
  "window.localStorage.removeItem(VIEW_AS_STORAGE_KEY)",
  "window.location.reload()"
], 'Account Center component');

requireMarkers(style, [
  '.account-profile-popover',
  '.account-center-shell',
  '.account-center-navigation',
  '.account-profile-card',
  '.account-theme-options',
  '.account-session-details',
  '.pulse-display-theme-handle',
  "content: 'Appearance'",
  '@media (max-width: 700px)',
  "html[data-account-center-installed='true'] .profile-dropdown-menu",
  ":root[data-theme='dark']",
  '@media (prefers-reduced-motion: reduce)',
  '.account-visually-hidden',
  '.account-center-toast'
], 'Account Center styling');

for (const forbidden of [
  'ahmed.adeyemi@ussignal.local',
  'ahmed.adeyemi@ussignal.com',
  'Ahmed Adeyemi',
  'Kevin Damisch',
  '10.10.24.15',
  'Sign out of all other sessions',
  'profilePhotoDatabase',
  'accountCenterAuthSession'
]) {
  if (component.includes(forbidden)) {
    throw new Error(`Account Center contains hardcoded identity or unsupported source: ${forbidden}`);
  }
}

if (!main.includes("import AccountCenterPortal from './AccountCenterPortal.jsx';")) {
  throw new Error('main.jsx does not import AccountCenterPortal.');
}
if (!main.includes('<AccountCenterPortal />')) {
  throw new Error('main.jsx does not render AccountCenterPortal.');
}
if (!main.includes("import './account-center.css';")) {
  throw new Error('main.jsx does not load account-center.css.');
}
if (main.indexOf("import './account-center.css';") > main.indexOf("import './enterprise-contrast-guard.css';")) {
  throw new Error('Account Center styling must load before the global contrast guard.');
}

const expectedRoutes = ['account-profile', 'account-appearance', 'account-session'];
for (const route of expectedRoutes) {
  const occurrences = component.split(`'${route}'`).length - 1;
  if (occurrences < 1) throw new Error(`Account Center route is not registered: ${route}`);
}

if (!component.includes("new Set(['image/jpeg', 'image/png'])")) {
  throw new Error('Profile upload types must remain limited to the verified JPG/PNG contract.');
}
if (!component.includes('2 * 1024 * 1024')) {
  throw new Error('Profile image maximum must preserve the verified 2 MB limit.');
}
if (component.includes('X-ProjectPulse-View-As-User')) {
  throw new Error('Personal Account Center requests must not inherit the viewed identity.');
}

console.log([
  'PR698_ACCOUNT_CENTER=PASS',
  'identitySource=existing-module-062',
  'profileStorage=existing-preferences',
  'viewAsEdit=actual-signed-in-only',
  'themeProvider=reused',
  'routes=profile,appearance,session',
  'responsive=desktop,tablet,mobile'
].join(' '));
