import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { JOURNEY_ROUTE, validateJourneys } from '../src/role-journeys/role-journeys.js';

const root = fileURLToPath(new URL('../', import.meta.url));
export const APP_ANCHORS = Object.freeze({
  guide: "import SystemUserGuide from './SystemUserGuide.Module001.g.jsx';",
  route: "function getRouteFromHash() {\n  const hash = window.location.hash || '#dashboard';\n  return hash.replace('#', '') || 'dashboard';\n}",
  navigation: '<nav className="enterprise-top-navigation" aria-label="Workspace navigation">'
});
const MARKER = 'MY_ROLE_IN_PULSE_ROUTE_V1';
function exactlyOnce(source, needle, replacement, label) {
  const count = source.split(needle).length - 1;
  if (count !== 1) throw new Error(`[role-journeys] Expected one ${label}; found ${count}.`);
  return source.replace(needle, replacement);
}
export function transformJourneyApp(source) {
  if (source.includes(`/* ${MARKER} */`)) {
    if (!source.includes("import SystemUserGuide from './role-journeys/RoleJourneyGuideRouter.jsx';")
      || !source.includes(`return route === '${JOURNEY_ROUTE}' ? 'user-guide' : route;`)
      || !source.includes('data-role-journeys-launch="true"')) {
      throw new Error('[role-journeys] Partial application integration.');
    }
    return source;
  }
  let code = exactlyOnce(source, APP_ANCHORS.guide,
    "import SystemUserGuide from './role-journeys/RoleJourneyGuideRouter.jsx';", 'generated guide import');
  code = exactlyOnce(code, APP_ANCHORS.route,
    `function getRouteFromHash() {\n  const hash = window.location.hash || '#dashboard';\n  const route = hash.replace('#', '') || 'dashboard';\n  return route === '${JOURNEY_ROUTE}' ? 'user-guide' : route;\n}`, 'hash route parser');
  code = exactlyOnce(code, APP_ANCHORS.navigation,
    `${APP_ANCHORS.navigation}\n          <a href="#${JOURNEY_ROUTE}" data-module-number="999" data-role-journeys-launch="true" aria-current={window.location.hash === '#${JOURNEY_ROUTE}' ? 'page' : undefined}>My Role in Pulse</a>`, 'authenticated navigation');
  return `/* ${MARKER} */\n${code}`;
}
export function transformJourneyRegistry(source) {
  const alias = `  '${JOURNEY_ROUTE}': 'user-guide',`;
  if (source.includes(alias)) {
    if (source.split(alias).length !== 2) throw new Error('[role-journeys] Duplicate route alias.');
    return source;
  }
  return exactlyOnce(source, 'const ROUTE_ALIASES = Object.freeze({',
    `const ROUTE_ALIASES = Object.freeze({\n${alias}`, 'module alias registry');
}
export function verifyJourneySources(webRoot = root) {
  const registry = fs.readFileSync(path.join(webRoot, 'src/module-availability-registry.js'), 'utf8');
  const routes = new Set([...registry.matchAll(/\broute:\s*'([^']+)'/g)].map((match) => match[1]));
  const errors = validateJourneys(routes);
  if (!routes.has('user-guide')) errors.push('Module 999 guide route missing');
  if (errors.length) throw new Error(`[role-journeys] ${errors.join('; ')}`);
  transformJourneyRegistry(registry);
  // Validate the source-equivalent anchors before bundling generated output.
  const app = fs.readFileSync(path.join(webRoot, 'src/App.jsx'), 'utf8')
    .replace("import SystemUserGuide from './SystemUserGuide.jsx';", APP_ANCHORS.guide);
  transformJourneyApp(app);
}
export default function roleJourneysPlugin() {
  return {
    name: 'my-role-in-pulse-visual-journeys', enforce: 'pre',
    buildStart() { verifyJourneySources(); },
    transform(code, id) {
      const filename = id.split('?')[0].replaceAll('\\', '/');
      if (filename.endsWith('/App.Module001.g.jsx')) return { code: transformJourneyApp(code), map: null };
      if (filename.endsWith('/module-availability-registry.js')) return { code: transformJourneyRegistry(code), map: null };
      return null;
    }
  };
}
