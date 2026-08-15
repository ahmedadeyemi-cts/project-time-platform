/* ENTERPRISE_NAVIGATION_PARITY_V1
 * Attribute-only compatibility layer. React continues to own the navigation
 * and module-card children; this layer only keeps the global More trigger
 * visible and publishes presentation metadata for the enterprise CSS.
 */

const INSTALL_MARKER = '__pulseEnterpriseNavigationParityInstalled';
const MORE_VISIBILITY_VALUE = 'persistent';
const MODULE_ICON_VALUE = 'enterprise';
const MODULE_006_BRAND_VALUE = 'toyota-hyundai-turion-space';
const HIDDEN_ATTRIBUTES = Object.freeze([
  'hidden',
  'aria-hidden',
  'data-projectpulse-permission-hidden',
  'data-module-availability-hidden'
]);

let applyFrame = 0;
let observer = null;

function restoreElementVisibility(element) {
  if (!(element instanceof HTMLElement)) return;

  if (element.hidden) element.hidden = false;
  for (const attribute of HIDDEN_ATTRIBUTES) {
    if (element.hasAttribute(attribute)) element.removeAttribute(attribute);
  }

  if (element.style.display === 'none') element.style.removeProperty('display');
  if (element.style.visibility === 'hidden') element.style.removeProperty('visibility');
  if (element.style.opacity === '0') element.style.removeProperty('opacity');
}

function restoreMoreTrigger() {
  const container = document.querySelector('.enterprise-top-navigation > .enterprise-more-navigation');
  const button = container?.querySelector(':scope > .enterprise-more-button');
  if (!container || !button) return;

  restoreElementVisibility(container);
  restoreElementVisibility(button);

  if (container.dataset.pulseMoreVisibility !== MORE_VISIBILITY_VALUE) {
    container.dataset.pulseMoreVisibility = MORE_VISIBILITY_VALUE;
  }
  if (button.dataset.pulseMoreVisibility !== MORE_VISIBILITY_VALUE) {
    button.dataset.pulseMoreVisibility = MORE_VISIBILITY_VALUE;
  }

  if (button.getAttribute('aria-label') !== 'Open pages available to the current effective user') {
    button.setAttribute('aria-label', 'Open pages available to the current effective user');
  }
  if (button.title !== 'Pages available to your current role or View-As identity') {
    button.title = 'Pages available to your current role or View-As identity';
  }
}

function annotateModuleCards() {
  document.querySelectorAll('.modules-directory-card[data-module-number]').forEach((card) => {
    if (!(card instanceof HTMLElement)) return;

    if (card.dataset.pulseModuleIcon !== MODULE_ICON_VALUE) {
      card.dataset.pulseModuleIcon = MODULE_ICON_VALUE;
    }

    const moduleNumber = String(card.dataset.moduleNumber || '').trim().toUpperCase();
    if (moduleNumber !== '006') return;

    if (card.dataset.customerBrandLogos !== MODULE_006_BRAND_VALUE) {
      card.dataset.customerBrandLogos = MODULE_006_BRAND_VALUE;
    }

    const title = 'Toyota, Hyundai, and Turion Space customer pipelines';
    if (card.title !== title) card.title = title;

    const heading = card.querySelector('h2')?.textContent?.trim() || 'Customer Pipelines';
    const accessibleName = `Module 006 — ${heading}. Customer logos: Toyota, Hyundai, and Turion Space.`;
    if (card.getAttribute('aria-label') !== accessibleName) {
      card.setAttribute('aria-label', accessibleName);
    }
  });
}

function applyEnterpriseNavigationParity() {
  restoreMoreTrigger();
  annotateModuleCards();
}

function scheduleEnterpriseNavigationParity() {
  window.cancelAnimationFrame(applyFrame);
  applyFrame = window.requestAnimationFrame(applyEnterpriseNavigationParity);
}

function installEnterpriseNavigationParity() {
  if (window[INSTALL_MARKER]) return;
  window[INSTALL_MARKER] = true;

  applyEnterpriseNavigationParity();

  observer = new MutationObserver((mutations) => {
    const needsRefresh = mutations.some((mutation) => (
      mutation.type === 'childList'
      || mutation.attributeName === 'hidden'
      || mutation.attributeName === 'aria-hidden'
      || mutation.attributeName === 'class'
      || mutation.attributeName === 'data-projectpulse-permission-hidden'
      || mutation.attributeName === 'data-module-availability-hidden'
    ));
    if (needsRefresh) scheduleEnterpriseNavigationParity();
  });

  observer.observe(document.documentElement, {
    childList: true,
    subtree: true,
    attributes: true,
    attributeFilter: [
      'hidden',
      'aria-hidden',
      'class',
      'data-projectpulse-permission-hidden',
      'data-module-availability-hidden'
    ]
  });

  for (const eventName of [
    'hashchange',
    'pageshow',
    'projectpulse:auth-session-ready',
    'projectpulse:view-as-changed',
    'projectpulse:permission-navigation-updated',
    'projectpulse:module-availability-loaded',
    'projectpulse:module-availability-changed',
    'projectpulse:experience-changed'
  ]) {
    window.addEventListener(eventName, scheduleEnterpriseNavigationParity);
  }
}

if (typeof window !== 'undefined' && typeof document !== 'undefined') {
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', installEnterpriseNavigationParity, { once: true });
  } else {
    installEnterpriseNavigationParity();
  }
}

export {
  applyEnterpriseNavigationParity,
  scheduleEnterpriseNavigationParity
};
