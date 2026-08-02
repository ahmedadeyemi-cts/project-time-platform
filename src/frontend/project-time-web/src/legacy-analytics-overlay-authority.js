const RETIRED_MODULE_030_IDS = Object.freeze([
  'projectpulse-030-shell',
  'projectpulse-030-reporting-card'
]);
const STYLE_ID = 'projectpulse-030-native-react-route-style';
const AUTHORITY_MARKER = 'MODULE_030_NATIVE_REACT_ROUTE';
let observer = null;
let cleanupScheduled = false;

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
  installNativeRouteStyle();
  for (const id of RETIRED_MODULE_030_IDS) {
    document.getElementById(id)?.remove();
  }

  const route = String(window.location.hash || '#dashboard')
    .replace(/^#/, '')
    .split('?')[0]
    .trim();
  if (['reporting', 'analytics', 'analytics-center', 'reports', 'financial-report-center', 'enterprise-reporting'].includes(route)) {
    document.documentElement.style.overflow = '';
    document.body?.style.removeProperty('overflow');
  }

  window.__projectPulse030Installed = true;
  window.__projectPulse030NativeReactRoute = true;
  window.__projectPulse030RuntimeAuthority = AUTHORITY_MARKER;
}

function scheduleCleanup() {
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

installNativeRouteStyle();
startObserver();
scheduleCleanup();

document.addEventListener('DOMContentLoaded', scheduleCleanup);
window.addEventListener('hashchange', scheduleCleanup);
window.addEventListener('pageshow', scheduleCleanup);
window.addEventListener('popstate', scheduleCleanup);
