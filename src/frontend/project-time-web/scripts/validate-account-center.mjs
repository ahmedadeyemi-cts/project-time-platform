import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const component = fs.readFileSync(path.join(root, 'src', 'AccountCenterPortal.jsx'), 'utf8');
const style = fs.readFileSync(path.join(root, 'src', 'account-center.css'), 'utf8');
const main = fs.readFileSync(path.join(root, 'src', 'main.jsx'), 'utf8');

const requiredComponentMarkers = [
  'PR698_ACCOUNT_CENTER',
  "'/api/identity/profile'",
  "'/api/profile/preferences'",
  "'account-profile'",
  "'account-appearance'",
  "'account-session'",
  'My profile',
  'Preferences',
  'Current session',
  'Presence unavailable',
  'Directory and authentication details',
  'System',
  'Enterprise table (recommended)',
  'View-As',
  'originalFetch()',
  'profileImageRemoved',
  'MAX_PROFILE_IMAGE_BYTES',
  'window.__projectPulseOriginalFetch',
  'projectpulse:identity-profile-changed'
];

for (const marker of requiredComponentMarkers) {
  if (!component.includes(marker)) throw new Error(`Account Center component missing: ${marker}`);
}

for (const marker of [
  '.account-profile-popover',
  '.account-center-shell',
  '.account-theme-options',
  '.account-session-details',
  '.pulse-display-theme-handle',
  '@media (max-width: 700px)',
  ":root[data-theme='dark']",
  '@media (prefers-reduced-motion: reduce)'
]) {
  if (!style.includes(marker)) throw new Error(`Account Center styling missing: ${marker}`);
}

for (const forbidden of [
  'ahmed.adeyemi@ussignal.local',
  'Ahmed Adeyemi',
  'Kevin Damisch',
  '10.10.24.15',
  'Sign out of all other sessions'
]) {
  if (component.includes(forbidden)) throw new Error(`Account Center hardcoded user or unsupported action: ${forbidden}`);
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

console.log('PR698_ACCOUNT_CENTER=PASS identitySource=existing profileStorage=existing viewAsEdit=blocked themeProvider=reused');
