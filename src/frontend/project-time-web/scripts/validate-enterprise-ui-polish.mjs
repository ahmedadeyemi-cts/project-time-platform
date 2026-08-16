import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');
const fail = (message) => { throw new Error(`ENTERPRISE_UI_POLISH_FAILED=${message}`); };

const runtime = read('src/enterprise-ui-polish.js');
const css = read('src/enterprise-ui-polish.css');
const followupCss = read('src/celar-ai-control-followup.css');
const askOperationsInjector = read('scripts/inject-celar-ai-ask-operations.mjs');
const brandAsset = read('src/assets/module-006-customer-brands.svg');
const main = read('src/main.jsx');

for (const marker of [
  'ENTERPRISE_UI_POLISH_RUNTIME_V1',
  "const MORE_MENU_ID = 'enterprise-more-navigation-menu';",
  "const APPEARANCE_HANDLE_SELECTOR = '.pulse-display-view-handle';",
  "document.addEventListener('pointerdown', handlePointerDown, true);",
  "document.addEventListener('keydown', handleKeyDown, true);",
  'event.composedPath()',
  'open.trigger.click()',
  "appearanceHandle.setAttribute('aria-label', 'Open appearance settings')",
  "launcher.setAttribute('aria-haspopup', 'dialog')",
  "module006.dataset.customerBrandAsset = 'verified-vector'"
]) {
  if (!runtime.includes(marker)) fail(`runtime_marker_missing:${marker}`);
}

for (const forbidden of [
  'appendChild(',
  'replaceChildren(',
  'innerHTML',
  'insertAdjacentHTML(',
  'window.location.reload()'
]) {
  if (runtime.includes(forbidden)) fail(`react_ownership_or_reload_forbidden:${forbidden}`);
}

for (const marker of [
  'ENTERPRISE_UI_POLISH_PRESENTATION_V1',
  "content: 'Appearance';",
  "url('./assets/module-006-customer-brands.svg')",
  '.help-launcher::before',
  '.help-panel.pulse-ai-help-panel.pulse-ai-system-chat.celar-ai-contextual-chat',
  '.celar-ai-chat-window-controls button',
  '.celar-ai-contextual-chat .help-messages',
  '.celar-ai-chat-attachments',
  '.celar-ai-contextual-chat .help-input-row',
  'main.app-shell.enterprise-nav-enabled > :is(',
  'body.projectpulse-route-audit-history .admin-runtime-stability-route-root',
  "[data-theme='dark']",
  '@media (max-width: 620px)',
  '@media print'
]) {
  if (!css.includes(marker)) fail(`css_marker_missing:${marker}`);
}

for (const marker of [
  'CELAR_AI_CONTROL_FOLLOWUP_V1',
  '.pulse-display-view-handle::after',
  "content: 'Appearance';",
  '.help-celar-operations-button',
  '.help-celar-health-button',
  'grid-template-columns: repeat(6, minmax(0, 1fr))',
  '.help-panel.pulse-ai-help-panel.pulse-ai-system-chat.celar-ai-contextual-chat.is-size-compact:not(.is-minimized)',
  '.help-panel.pulse-ai-help-panel.pulse-ai-system-chat.celar-ai-contextual-chat.is-size-standard:not(.is-minimized)',
  '.help-panel.pulse-ai-help-panel.pulse-ai-system-chat.celar-ai-contextual-chat.is-size-wide:not(.is-minimized)',
  '.help-panel.pulse-ai-help-panel.pulse-ai-system-chat.celar-ai-contextual-chat.is-size-fullscreen:not(.is-minimized)',
  '.help-panel.pulse-ai-help-panel.pulse-ai-system-chat.celar-ai-contextual-chat.is-minimized',
  'pointer-events: auto !important;',
  'touch-action: manipulation;',
  "[data-theme='dark']",
  '@media (max-width: 900px) and (min-width: 621px)',
  '@media (max-width: 620px)'
]) {
  if (!followupCss.includes(marker)) fail(`followup_css_marker_missing:${marker}`);
}

for (const marker of [
  'Troubleshoot with Celar',
  'help-celar-operations-button" onClick={openOperations}>Troubleshoot with Celar</button>',
  'help-celar-health-button" onClick={openHealthAutomation}>Health &amp; Automatic Defects</button>',
  'Open guided defect questionnaire'
]) {
  if (!askOperationsInjector.includes(marker)) fail(`ask_operations_marker_missing:${marker}`);
}

if (askOperationsInjector.includes('help-celar-operations-button" onClick={openOperations}>Troubleshoot with Ask Celar AI</button>')) {
  fail('legacy_troubleshoot_button_label_retained');
}

for (const marker of [
  '<title id="module006BrandTitle">Toyota, Hyundai, and Turion Space customer brands</title>',
  'fill="#EB0A1E"',
  'fill="#002C5F"',
  'TOYOTA',
  'HYUNDAI',
  'TURION',
  'SPACE',
  'M12 3.848C5.223 3.848',
  'M11.999 18.145c6.627 0 12.001-2.751'
]) {
  if (!brandAsset.includes(marker)) fail(`brand_asset_marker_missing:${marker}`);
}

if (!main.includes("import './enterprise-ui-polish.js';")) {
  fail('runtime_import_missing');
}
if (!main.includes("import './enterprise-ui-polish.css';")) {
  fail('css_import_missing');
}
if (!main.includes("import './celar-ai-control-followup.css';")) {
  fail('followup_css_import_missing');
}

const runtimeImportIndex = main.indexOf("import './enterprise-ui-polish.js';");
const appImportIndex = main.indexOf("import App from './App.Module001.g.jsx';");
if (runtimeImportIndex < 0 || appImportIndex < 0 || runtimeImportIndex > appImportIndex) {
  fail('runtime_must_load_before_react_app');
}

const cssImportIndex = main.indexOf("import './enterprise-ui-polish.css';");
const followupCssImportIndex = main.indexOf("import './celar-ai-control-followup.css';");
const contrastGuardIndex = main.indexOf("import './enterprise-contrast-guard.css';");
if (cssImportIndex < 0 || followupCssImportIndex < 0 || contrastGuardIndex < 0) {
  fail('stylesheet_import_missing');
}
if (followupCssImportIndex < cssImportIndex || followupCssImportIndex > contrastGuardIndex) {
  fail('followup_css_must_load_after_polish_and_before_contrast_guard');
}
if (cssImportIndex > contrastGuardIndex) {
  fail('contrast_guard_must_remain_last');
}

console.log('ENTERPRISE_UI_POLISH=PASS appearance=all-modes logos=verified-vectors celar=enterprise controls=restored quick-actions=balanced more=click-away route-flow=compact react-ownership=preserved');
