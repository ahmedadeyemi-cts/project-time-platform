import { useEffect } from 'react';
import './critical-route-presentation.css';

const PREFIX = 'projectpulse-route-';
const ENTERPRISE_HEADER_ATTRIBUTE = 'data-enterprise-page-header';
const ENTERPRISE_HEADER_OWNER_ATTRIBUTE = 'data-enterprise-page-header-owned';

const ROUTE_ROOT_SELECTORS = [
  '[data-module]',
  '[data-module-code]',
  '[data-module-number]',
  '[data-module-id]',
  '[data-brand="us-signal"]',
  '[class$="-center"]',
  '[class*="-center "]',
  '[class$="-workspace"]',
  '[class*="-workspace "]',
  '[class$="-dashboard"]',
  '[class*="-dashboard "]',
  '[class$="-page"]',
  '[class*="-page "]'
];

const ROUTE_HEADER_SELECTOR = ROUTE_ROOT_SELECTORS
  .map((selector) => `${selector} > header`)
  .join(', ');

const EXCLUDED_HEADER_ANCESTORS = [
  '[role="dialog"]',
  '[aria-modal="true"]',
  'aside',
  'nav',
  '[class*="drawer"]',
  '[class*="popover"]',
  '[class*="modal"]'
].join(', ');

function currentRoute() {
  return window.location.hash.replace(/^#/, '').split('?')[0] || 'dashboard';
}

function isRoutePageHeader(header) {
  if (!(header instanceof HTMLElement)) return false;
  if (!header.querySelector('h1, h2')) return false;
  if (header.closest(EXCLUDED_HEADER_ANCESTORS)) return false;
  return true;
}

function adoptEnterprisePageHeaders(root = document) {
  const headers = [];

  if (root instanceof Element && root.matches(ROUTE_HEADER_SELECTOR)) {
    headers.push(root);
  }

  if ('querySelectorAll' in root) {
    headers.push(...root.querySelectorAll(ROUTE_HEADER_SELECTOR));
  }

  for (const header of headers) {
    if (!isRoutePageHeader(header)) continue;
    if (header.getAttribute(ENTERPRISE_HEADER_ATTRIBUTE) === 'true') continue;

    header.setAttribute(ENTERPRISE_HEADER_ATTRIBUTE, 'true');
    header.setAttribute(ENTERPRISE_HEADER_OWNER_ATTRIBUTE, 'true');
  }
}

function clearOwnedEnterprisePageHeaders() {
  document
    .querySelectorAll(`[${ENTERPRISE_HEADER_OWNER_ATTRIBUTE}="true"]`)
    .forEach((header) => {
      header.removeAttribute(ENTERPRISE_HEADER_ATTRIBUTE);
      header.removeAttribute(ENTERPRISE_HEADER_OWNER_ATTRIBUTE);
    });
}

export default function CriticalRoutePresentationBoundary() {
  useEffect(() => {
    let scheduledFrame = 0;

    const scheduleHeaderAdoption = () => {
      if (scheduledFrame) return;
      scheduledFrame = window.requestAnimationFrame(() => {
        scheduledFrame = 0;
        adoptEnterprisePageHeaders(document);
      });
    };

    const apply = () => {
      for (const className of [...document.body.classList]) {
        if (className.startsWith(PREFIX)) document.body.classList.remove(className);
      }
      document.body.classList.add(`${PREFIX}${currentRoute()}`);
      scheduleHeaderAdoption();
    };

    const observerRoot = document.getElementById('root') ?? document.body;
    const observer = new MutationObserver(scheduleHeaderAdoption);
    observer.observe(observerRoot, { childList: true, subtree: true });

    apply();
    window.addEventListener('hashchange', apply);

    return () => {
      window.removeEventListener('hashchange', apply);
      observer.disconnect();
      if (scheduledFrame) window.cancelAnimationFrame(scheduledFrame);
      clearOwnedEnterprisePageHeaders();

      for (const className of [...document.body.classList]) {
        if (className.startsWith(PREFIX)) document.body.classList.remove(className);
      }
    };
  }, []);

  return null;
}
