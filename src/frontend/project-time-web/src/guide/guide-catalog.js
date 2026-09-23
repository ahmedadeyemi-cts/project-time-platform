import { compareProjectPulseModules } from '../module-ordering.js';
import { GUIDE_BY_ROUTE, EXTRA_GUIDE_MODULES, ROLE_REFERENCE } from './guide-content.js';
import { safeGuideRoute } from './guide-model.js';

// Catalog identity is documentation metadata, never authorization. Canonical
// entries precede legacy registry aliases so duplicate/smoke labels cannot win.
export function buildGuideCatalog(canonical = [], installed = []) {
  const byRoute = new Map();
  for (const module of [...canonical, ...EXTRA_GUIDE_MODULES, ...installed]) {
    const route = String(module?.route || '').replace(/^#/, '');
    if (!safeGuideRoute(route) || byRoute.has(route)) continue;
    const number = String(module.moduleNumber || module.navLabel || '').replace(/^MODULE\s+/i, '').trim();
    const moduleNumber = /^\d{3}[A-Z]*$/.test(number) ? number : '';
    byRoute.set(route, {
      route, moduleNumber,
      title: route === 'toyota-hyundai-pipelines' ? 'Customer Pipelines' : module.displayName || module.title || route,
      group: module.group || 'Other installed workspaces',
      guide: GUIDE_BY_ROUTE[route] || null,
      audienceLabels: (GUIDE_BY_ROUTE[route]?.roles || []).map(code => ROLE_REFERENCE.find(role => role.code === code)?.title || (code === '*' ? 'Everyone' : code))
    });
  }
  return [...byRoute.values()].sort(compareProjectPulseModules);
}
