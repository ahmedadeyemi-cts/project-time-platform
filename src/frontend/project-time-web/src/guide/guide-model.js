import { withCompletionGuide } from './guide-completion.js';
// Learning content never grants permissions or changes application state.
export const GUIDE_BASELINE = '4c31007053359b1cf41681d686742f6c08fbb3b6';
export const GUIDE_EDITION = '2026.09.23';
export const ALL = ['*'];
export const ADMIN = ['ADMINISTRATOR', 'SUPER_ADMINISTRATOR'];
export const DELIVERY = ['PROJECT_MANAGEMENT', 'PROJECT_MANAGEMENT_LEAD', 'PROJECT_TEAM_COORDINATOR', 'PROJECT_COORDINATOR', 'ENGINEERING_LEAD', ...ADMIN];
export const FINANCE = ['ACCOUNTING', 'BILLING', 'PROJECT_TEAM_COORDINATOR', ...ADMIN];
export const SALES = ['SALES', 'INSIDE_SALES', 'SOLUTION_ARCHITECT', 'SOLUTION_ARCHITECT_MANAGER', ...ADMIN];
export const how = (title, steps, outcome, handoff) => ({ title, steps, outcome, handoff });
export const topic = (route, roles, responsibility, prerequisites, procedures, limitations, sources) => withCompletionGuide({
  route, roles, responsibility, prerequisites, procedures, limitations,
  sources: sources.map(path => path.startsWith('docs/') || path.startsWith('src/') || path.startsWith('scripts/') ? path : `src/frontend/project-time-web/src/${path}`)
});
export function flattenGuide(value) {
  if (Array.isArray(value)) return value.map(flattenGuide).join(' ');
  if (value && typeof value === 'object') return Object.values(value).map(flattenGuide).join(' ');
  return String(value ?? '');
}
export function safeGuideRoute(route) { return typeof route === 'string' && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(route); }
export function guideLinkAllowed(route, context) {
  return Boolean(safeGuideRoute(route) && context?.accessReady && context?.state === 'ready' &&
    context.modules?.some(module => module.route === route));
}
export function filterGuideEntries(entries, { query = '', category = '*', role = '*', availableOnly = false, context } = {}) {
  const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
  return entries.filter(entry => (category === '*' || entry.group === category) &&
    (role === '*' || role === 'SUPER_ADMINISTRATOR' || entry.guide?.roles.includes('*') || entry.guide?.roles.includes(role)) &&
    (!availableOnly || guideLinkAllowed(entry.route, context)) &&
    terms.every(term => flattenGuide(entry).toLowerCase().includes(term)));
}
