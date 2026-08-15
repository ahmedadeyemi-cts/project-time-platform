/* ENTERPRISE_UI_POLISH_RUNTIME_V1
 * Presentation-only coordination for the enterprise Appearance handle,
 * Celar AI launcher metadata, and click-away dismissal of the React-owned
 * permission-aware More menu. No navigation children or authorization data
 * are inserted, removed, or replaced here.
 */

const INSTALL_MARKER = '__pulseEnterpriseUiPolishRuntimeInstalled';
const MORE_MENU_ID = 'enterprise-more-navigation-menu';
const APPEARANCE_HANDLE_SELECTOR = '.pulse-display-view-handle';
const CELAR_LAUNCHER_SELECTOR = '.help-launcher';
let refreshFrame = 0;
let observer = null;

function openMoreElements() {
  const trigger = document.querySelector('.enterprise-more-button[aria-expanded="true"]');
  const menu = document.getElementById(MORE_MENU_ID);

  if (!(trigger instanceof HTMLButtonElement) || !(menu instanceof HTMLElement)) return null;
  return { trigger, menu };
}

function closeMoreMenu({ restoreFocus = false } = {}) {
  const open = openMoreElements();
  if (!open) return false;

  open.trigger.click();
  if (restoreFocus) {
    window.requestAnimationFrame(() => open.trigger.focus());
  }
  return true;
}

function annotatePresentationControls() {
  const appearanceHandle = document.querySelector(APPEARANCE_HANDLE_SELECTOR);
  if (appearanceHandle instanceof HTMLButtonElement) {
    if (appearanceHandle.dataset.pulseAppearanceHandle !== 'true') {
      appearanceHandle.dataset.pulseAppearanceHandle = 'true';
    }
    if (appearanceHandle.getAttribute('aria-label') !== 'Open appearance settings') {
      appearanceHandle.setAttribute('aria-label', 'Open appearance settings');
    }
    if (appearanceHandle.title !== 'Open appearance settings') {
      appearanceHandle.title = 'Open appearance settings';
    }
  }

  const launcher = document.querySelector(CELAR_LAUNCHER_SELECTOR);
  if (launcher instanceof HTMLButtonElement) {
    if (launcher.dataset.pulseCelarLauncher !== 'enterprise') {
      launcher.dataset.pulseCelarLauncher = 'enterprise';
    }
    if (launcher.getAttribute('aria-haspopup') !== 'dialog') {
      launcher.setAttribute('aria-haspopup', 'dialog');
    }
    if (launcher.title !== 'Open Ask Celar AI') {
      launcher.title = 'Open Ask Celar AI';
    }
  }

  const panel = document.getElementById('celar-ai-global-chat');
  if (panel instanceof HTMLElement && panel.dataset.pulseEnterpriseAssistant !== 'true') {
    panel.dataset.pulseEnterpriseAssistant = 'true';
  }

  const module006 = document.querySelector('.modules-directory-card[data-module-number="006"]');
  if (module006 instanceof HTMLElement) {
    if (module006.dataset.customerBrandAsset !== 'verified-vector') {
      module006.dataset.customerBrandAsset = 'verified-vector';
    }
    const label = 'Module 006 — Customer Pipelines. Customer brands: Toyota, Hyundai, and Turion Space.';
    if (module006.getAttribute('aria-label') !== label) {
      module006.setAttribute('aria-label', label);
    }
  }
}

function schedulePresentationRefresh() {
  window.cancelAnimationFrame(refreshFrame);
  refreshFrame = window.requestAnimationFrame(annotatePresentationControls);
}

function handlePointerDown(event) {
  const open = openMoreElements();
  if (!open) return;

  const path = typeof event.composedPath === 'function' ? event.composedPath() : [];
  if (path.includes(open.trigger) || path.includes(open.menu)) return;

  closeMoreMenu();
}

function handleKeyDown(event) {
  if (event.key !== 'Escape') return;
  if (closeMoreMenu({ restoreFocus: true })) event.preventDefault();
}

function handleRouteChange() {
  closeMoreMenu();
  schedulePresentationRefresh();
}

function installEnterpriseUiPolishRuntime() {
  if (window[INSTALL_MARKER]) return;
  window[INSTALL_MARKER] = true;

  annotatePresentationControls();

  observer = new MutationObserver((mutations) => {
    const needsRefresh = mutations.some((mutation) => (
      mutation.type === 'childList'
      || mutation.attributeName === 'class'
      || mutation.attributeName === 'aria-expanded'
    ));
    if (needsRefresh) schedulePresentationRefresh();
  });

  observer.observe(document.documentElement, {
    childList: true,
    subtree: true,
    attributes: true,
    attributeFilter: ['class', 'aria-expanded']
  });

  document.addEventListener('pointerdown', handlePointerDown, true);
  document.addEventListener('keydown', handleKeyDown, true);
  window.addEventListener('hashchange', handleRouteChange);
  window.addEventListener('pageshow', handleRouteChange);
  window.addEventListener('projectpulse:experience-changed', schedulePresentationRefresh);
}

if (typeof window !== 'undefined' && typeof document !== 'undefined') {
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', installEnterpriseUiPolishRuntime, { once: true });
  } else {
    installEnterpriseUiPolishRuntime();
  }
}

export {
  annotatePresentationControls,
  closeMoreMenu,
  schedulePresentationRefresh
};
