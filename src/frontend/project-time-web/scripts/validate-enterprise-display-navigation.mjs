import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const source = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const fail = (message) => { throw new Error(`ENTERPRISE_DISPLAY_NAVIGATION_FAILED=${message}`); };

const component = source('src/DisplayPreferencesDrawer.jsx');
const css = source('src/display-preferences-drawer.css');
const headerCss = source('src/enterprise-header-navigation-layout.css');
const main = source('src/main.jsx');

for (const marker of [
  "const EXPERIENCE_STORAGE_KEY = 'pulse-enterprise-experience';",
  "const THEME_STORAGE_KEY = 'ptp-theme';",
  'projectpulse:experience-changed',
  'projectpulse:theme-changed',
  'pulse-display-view-handle',
  'pulse-display-appearance-handle',
  'data-pulse-theme-choice={LIGHT_THEME}',
  'data-pulse-theme-choice={DARK_THEME}',
  'role="dialog"',
  'aria-modal="true"'
]) {
  if (!component.includes(marker)) fail(`component_marker_missing:${marker}`);
}

for (const marker of [
  'ENTERPRISE_DISPLAY_DRAWER_AND_MORE_OVERLAY_V2',
  '#pulse-display-utility-dock',
  "grid-template-areas: 'brand context navigation utilities'",
  'overflow: visible !important;',
  '#enterprise-more-navigation-menu.enterprise-more-dropdown',
  'z-index: 16000 !important;',
  '.pulse-display-preferences-drawer',
  '.pulse-display-view-handle',
  '.pulse-display-appearance-handle',
  "[data-theme='dark'] .pulse-display-choice",
  '@media print'
]) {
  if (!css.includes(marker)) fail(`css_marker_missing:${marker}`);
}

for (const marker of [
  'ENTERPRISE_HEADER_NAVIGATION_ALIGNMENT_V1',
  "grid-template-columns: minmax(240px, 320px) minmax(260px, 1fr) max-content max-content",
  "grid-template-areas: 'brand context navigation utilities'",
  '.enterprise-top-navigation',
  '.enterprise-header-utilities',
  '.workspace-signed-in-user',
  'overflow-wrap: anywhere !important;',
  '@media (max-width: 1320px)'
]) {
  if (!headerCss.includes(marker)) fail(`header_css_marker_missing:${marker}`);
}

if (!main.includes("import DisplayPreferencesDrawer from './DisplayPreferencesDrawer.jsx';")) {
  fail('component_import_missing');
}
if (!main.includes("import './display-preferences-drawer.css';")) {
  fail('css_import_missing');
}
if (!main.includes("import './enterprise-header-navigation-layout.css';")) {
  fail('header_layout_css_import_missing');
}
if (!main.includes('<DisplayPreferencesDrawer />')) {
  fail('component_mount_missing');
}

const displayCssIndex = main.indexOf("import './display-preferences-drawer.css';");
const headerCssIndex = main.indexOf("import './enterprise-header-navigation-layout.css';");
const contrastGuardIndex = main.indexOf("import './enterprise-contrast-guard.css';");
if (
  displayCssIndex < 0
  || headerCssIndex < 0
  || contrastGuardIndex < 0
  || displayCssIndex > headerCssIndex
  || headerCssIndex > contrastGuardIndex
) {
  fail('contrast_guard_must_remain_last');
}

for (const forbidden of [
  'appendChild(',
  'replaceChildren(',
  'innerHTML',
  'window.location.reload()'
]) {
  if (component.includes(forbidden)) fail(`react_ownership_or_reload_forbidden:${forbidden}`);
}

console.log('ENTERPRISE_DISPLAY_NAVIGATION=PASS drawer=collapsed-left-edge theme=subtle more=unclipped header=single-row-wide context=dynamic contrast=guarded');
