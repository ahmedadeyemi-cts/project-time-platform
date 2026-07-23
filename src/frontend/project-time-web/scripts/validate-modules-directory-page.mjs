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
  portal: 'src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx',
  css: 'src/frontend/project-time-web/src/modules-directory-page.css',
  packageJson: 'src/frontend/project-time-web/package.json'
};

const [main, portal, css, packageJson] = await Promise.all(Object.values(paths).map(text));

requireText(main, [
  "import ModulesDirectoryPortal from './ModulesDirectoryPortal.jsx';",
  '<ModulesDirectoryPortal />',
  '<App />'
], 'Application root integration');

if (main.indexOf('<ModulesDirectoryPortal />') < main.indexOf('<App />')) {
  throw new Error('The Modules directory portal must render after App so the authorized navigation model already exists.');
}

requireText(portal, [
  "const MODULES_ROUTE = 'modules';",
  "const MODULES_HASH = '#modules';",
  "navigation.querySelector('#projectpulse-modules-navigation-link')",
  "candidate.getAttribute('href') === '#dashboard'",
  "dashboardLink.insertAdjacentElement('afterend', link)",
  "document.querySelectorAll('.enterprise-sidebar-section')",
  "cleanText(section.querySelector('.enterprise-sidebar-section-title')?.textContent).toLowerCase() === 'pinned'",
  "pinnedSection?.querySelectorAll('.enterprise-sidebar-links:not(.nested) > a[href^=\"#\"]')",
  "document.querySelectorAll('.enterprise-sidebar-group')",
  "document.querySelectorAll('.enterprise-sidebar-group-toggle')",
  "groupElement.querySelectorAll('.enterprise-sidebar-links.nested a[href^=\"#\"]')",
  "route === 'dashboard' || route === MODULES_ROUTE",
  "document.querySelector('main.app-shell.enterprise-nav-enabled')",
  "mutationOriginatesInsidePortal",
  "mutations.every(mutationOriginatesInsidePortal)",
  'moduleListsMatch(current, nextModules)',
  "window.addEventListener('projectpulse:view-as-changed', refresh)",
  "window.addEventListener('hashchange', handleHashChange)",
  'restoreNavigationGroups(expandedForDirectory.current)',
  'Search modules',
  'All categories',
  'Open module →',
  'createPortal('
], 'Role-aware Modules directory');

if (portal.includes('getInstalledProjectPulseModuleRegistry') || portal.includes('const modules = [')) {
  throw new Error('The Modules page must reuse authorized navigation output instead of duplicating the module registry.');
}

if (portal.includes('observer.observe(document.body')) {
  throw new Error('The Modules observer must not watch the whole body and retrigger itself from portal card rendering.');
}

requireText(css, [
  '#projectpulse-modules-navigation-link',
  'main.app-shell.route-modules > *:not(.top-bar):not(.enterprise-sidebar):not(#modules-directory-portal-host)',
  'main.app-shell.route-modules > #modules-directory-portal-host',
  '.modules-directory-page',
  '.modules-directory-grid',
  '.modules-directory-card'
], 'Modules page route isolation and styling');

requireText(packageJson, [
  'validate:modules-directory',
  'node ./scripts/validate-modules-directory-page.mjs',
  'npm run validate:modules-directory'
], 'Production build wiring');

console.log('Persistent Dashboard and Modules navigation, role-aware directory, filtering, shell lifecycle, and route-isolation contracts passed.');
