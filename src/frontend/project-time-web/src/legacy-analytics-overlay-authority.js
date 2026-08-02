const RETIRED_MODULE_030_IDS = Object.freeze([
  'projectpulse-030-shell',
  'projectpulse-030-reporting-card'
]);
const STYLE_ID = 'projectpulse-030-native-react-route-style';
const AUTHORITY_MARKER = 'MODULE_030_NATIVE_REACT_ROUTE';
const RUNTIME_ROUTE_ALIASES = Object.freeze({
  'celar-ai': 'work-task-builder',
  'pulse-ai': 'work-task-builder',
  'analytics': 'reporting',
  'analytics-center': 'reporting',
  'reports': 'reporting',
  'executive-reporting': 'reporting',
  'financial-report-center': 'reporting',
  'enterprise-reporting': 'reporting',
  'crm': 'crm-integration',
  'crm-erp': 'crm-integration',
  'crm-erp-integration': 'crm-integration',
  'crm-integration-center': 'crm-integration',
  'microsoft-integration': 'entra-secret-administration',
  'module-065': 'entra-secret-administration',
  'global-mail-configuration': 'entra-secret-administration',
  'psa-modules': 'toyota-hyundai-pipelines',
  'project-register': 'toyota-hyundai-pipelines',
  'project-manager-workload': 'project-workload',
  'project-management-workload': 'project-workload',
  'resource-assignment-handoff': 'signed-handoff'
});
let observer = null;
let cleanupScheduled = false;

function canonicalizeRuntimeHash() {
  const rawHash = String(window.location.hash || '#dashboard').replace(/^#/, '');
  const questionIndex = rawHash.indexOf('?');
  const rawRoute = (questionIndex >= 0 ? rawHash.slice(0, questionIndex) : rawHash).trim() || 'dashboard';
  const query = questionIndex >= 0 ? rawHash.slice(questionIndex) : '';
  const canonicalRoute = RUNTIME_ROUTE_ALIASES[rawRoute] || rawRoute;
  if (canonicalRoute === rawRoute) return canonicalRoute;

  const canonicalHash = `#${canonicalRoute}${query}`;
  window.history.replaceState(window.history.state, '', canonicalHash);
  window.dispatchEvent(new CustomEvent('projectpulse:route-canonicalized', {
    detail: { originalRoute: rawRoute, canonicalRoute, hash: canonicalHash }
  }));
  return canonicalRoute;
}

function installNativeRouteStyle() {
  if (document.getElementById(STYLE_ID)) return;
  const style = document.createElement('style');
  style.id = STYLE_ID;
  style.textContent = `
    #projectpulse-030-shell,
    #projectpulse-030-reporting-card {
      display: none !important;
      visibility: hidden !important;
      pointer-events: none !important;
    }
  `;
  document.head.appendChild(style);
}

function removeRetiredModule030Overlay() {
  const route = canonicalizeRuntimeHash();
  installNativeRouteStyle();
  for (const id of RETIRED_MODULE_030_IDS) {
    document.getElementById(id)?.remove();
  }

  if (route === 'reporting') {
    document.documentElement.style.overflow = '';
    document.body?.style.removeProperty('overflow');
  }

  window.__projectPulse030Installed = true;
  window.__projectPulse030NativeReactRoute = true;
  window.__projectPulse030RuntimeAuthority = AUTHORITY_MARKER;
  window.__projectPulseCanonicalRoute = route;
}

function scheduleCleanup() {
  canonicalizeRuntimeHash();
  if (cleanupScheduled) return;
  cleanupScheduled = true;
  queueMicrotask(() => {
    cleanupScheduled = false;
    removeRetiredModule030Overlay();
  });
  window.setTimeout(removeRetiredModule030Overlay, 80);
  window.setTimeout(removeRetiredModule030Overlay, 250);
}

function startObserver() {
  if (observer || !document.documentElement) return;
  observer = new MutationObserver((mutations) => {
    const retiredAdded = mutations.some((mutation) =>
      Array.from(mutation.addedNodes).some((node) =>
        node instanceof Element && (
          RETIRED_MODULE_030_IDS.includes(node.id)
          || RETIRED_MODULE_030_IDS.some((id) => node.querySelector?.(`#${id}`))
        )
      )
    );
    if (retiredAdded) scheduleCleanup();
  });
  observer.observe(document.documentElement, { childList: true, subtree: true });
}

canonicalizeRuntimeHash();
installNativeRouteStyle();
startObserver();
scheduleCleanup();

document.addEventListener('DOMContentLoaded', scheduleCleanup);
window.addEventListener('hashchange', scheduleCleanup);
window.addEventListener('pageshow', scheduleCleanup);
window.addEventListener('popstate', scheduleCleanup);
