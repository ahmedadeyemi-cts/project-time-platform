import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { moduleForRoute } from './module-availability-registry.js';

const EXPERIENCE_STORAGE_KEY = 'pulse-enterprise-experience';
const EXPERIENCE_EVENT = 'projectpulse:experience-changed';
const ENTERPRISE_EXPERIENCE = 'enterprise';
const CLASSIC_EXPERIENCE = 'classic';
const CONTROL_HOST_ID = 'pulse-enterprise-experience-control-host';
const PAGE_CHROME_HOST_ID = 'pulse-enterprise-page-chrome-host';
const DISPLAY_UTILITY_DOCK_ID = 'pulse-display-utility-dock';

const STATIC_ROUTE_METADATA = Object.freeze({
  dashboard: Object.freeze({
    group: 'Workspace',
    title: 'Dashboard',
    description: 'Your role-aware starting point for delivery, approvals, workload, notifications, and operational priorities.'
  }),
  modules: Object.freeze({
    group: 'Administration',
    title: 'Module Management',
    description: 'Find authorized workspaces, review access scope, and manage module availability from one enterprise directory.'
  })
});

function cleanText(value) {
  return String(value ?? '').replace(/\s+/g, ' ').trim();
}

function normalizeExperience(value) {
  return String(value || '').toLowerCase() === CLASSIC_EXPERIENCE
    ? CLASSIC_EXPERIENCE
    : ENTERPRISE_EXPERIENCE;
}

function readExperience() {
  try {
    return normalizeExperience(window.localStorage.getItem(EXPERIENCE_STORAGE_KEY));
  } catch {
    return ENTERPRISE_EXPERIENCE;
  }
}

function currentRoute() {
  return String(window.location.hash || '#dashboard').replace(/^#/, '').trim() || 'dashboard';
}

function humanizeRoute(route) {
  return cleanText(route)
    .replaceAll('-', ' ')
    .replace(/\b\w/g, (character) => character.toUpperCase()) || 'Workspace';
}

function applyExperience(experience, { announce = true } = {}) {
  const normalized = normalizeExperience(experience);
  try {
    window.localStorage.setItem(EXPERIENCE_STORAGE_KEY, normalized);
  } catch {
    // Browser storage can be unavailable in hardened or private sessions.
  }

  document.documentElement.dataset.pulseExperience = normalized;
  if (document.body) document.body.dataset.pulseExperience = normalized;

  if (announce) {
    window.dispatchEvent(new CustomEvent(EXPERIENCE_EVENT, {
      detail: { experience: normalized }
    }));
  }

  return normalized;
}

function routeMetadata(route) {
  const staticMetadata = STATIC_ROUTE_METADATA[route];
  if (staticMetadata) return { route, moduleNumber: '', ...staticMetadata };

  const module = moduleForRoute(route);
  if (module) {
    return {
      route,
      moduleNumber: module.moduleNumber,
      group: module.group || 'Pulse workspace',
      title: module.moduleNumber === '006' ? 'Customer Pipelines' : module.displayName,
      description: module.moduleNumber === '006'
        ? 'Manage governed pipeline records, ownership, estimates, updates, documents, and action items for Toyota, Hyundai, and every other customer.'
        : module.description || `Open and manage the ${module.displayName} workspace within your effective access scope.`
    };
  }

  return {
    route,
    moduleNumber: '',
    group: 'Pulse workspace',
    title: humanizeRoute(route),
    description: 'Use this governed workspace within the permissions and data scope assigned to your effective identity.'
  };
}

function findTopBar(main) {
  return main?.querySelector(':scope > .top-bar')
    || main?.querySelector(':scope > .enterprise-top-bar')
    || document.querySelector('.enterprise-top-bar, .top-bar');
}

function ensureDisplayUtilityDock(main) {
  const topBar = findTopBar(main);
  if (!topBar) return null;

  let dock = topBar.querySelector(`:scope > #${DISPLAY_UTILITY_DOCK_ID}`);
  if (!dock) {
    document.getElementById(DISPLAY_UTILITY_DOCK_ID)?.remove();
    dock = document.createElement('div');
    dock.id = DISPLAY_UTILITY_DOCK_ID;
    dock.className = 'pulse-display-utility-dock';
    dock.dataset.pulseDisplayUtilityDock = 'true';
    dock.setAttribute('aria-label', 'Display options');

    const label = document.createElement('span');
    label.className = 'pulse-display-utility-dock__label';
    label.textContent = 'Display';
    dock.appendChild(label);

    const utilities = topBar.querySelector(':scope > .enterprise-header-utilities');
    if (utilities) utilities.insertAdjacentElement('beforebegin', dock);
    else topBar.appendChild(dock);
  }

  const themeSwitcher = topBar.querySelector('[data-pulse-header-theme-switcher]');
  if (themeSwitcher && themeSwitcher.parentElement !== dock) dock.appendChild(themeSwitcher);
  return dock;
}

function ensureControlHost(main) {
  const dock = ensureDisplayUtilityDock(main);
  if (!dock) return null;

  let host = document.getElementById(CONTROL_HOST_ID);
  if (host && host.parentElement !== dock) host.remove();

  host = dock.querySelector(`#${CONTROL_HOST_ID}`);
  if (!host) {
    host = document.createElement('div');
    host.id = CONTROL_HOST_ID;
    host.dataset.pulseEnterpriseExperienceHost = 'control';
    dock.appendChild(host);
  }

  return host;
}

function ensurePageChromeHost(main) {
  if (!main) return null;

  let host = main.querySelector(`:scope > #${PAGE_CHROME_HOST_ID}`);
  if (!host) {
    document.getElementById(PAGE_CHROME_HOST_ID)?.remove();
    host = document.createElement('div');
    host.id = PAGE_CHROME_HOST_ID;
    host.dataset.pulseEnterpriseExperienceHost = 'page-chrome';

    const topBar = main.querySelector(':scope > .top-bar, :scope > .enterprise-top-bar');
    if (topBar) topBar.insertAdjacentElement('afterend', host);
    else main.prepend(host);
  }

  return host;
}

function isExcludedLegacyHeader(element) {
  return Boolean(element.closest([
    '.top-bar',
    '.enterprise-top-bar',
    '.enterprise-sidebar',
    `#${PAGE_CHROME_HOST_ID}`,
    '[role="dialog"]',
    '[aria-modal="true"]',
    '.drawer',
    '[class*="drawer"]',
    '.modal',
    '[class*="modal"]',
    '[class*="celar-ai"]',
    '[class*="help-assistant"]'
  ].join(', ')));
}

function decorateLegacyPageHeader(main, route) {
  if (!main) return;

  const explicitSelectors = route === 'modules'
    ? ['.modules-directory-hero']
    : [
      '.uss-enterprise-page-header',
      '.modules-directory-hero',
      '.hero',
      '.dashboard-hero',
      '.role-welcome-hero',
      '[class*="workspace-hero"]',
      '[class*="page-hero"]',
      '[class*="module-hero"]'
    ];

  const explicit = explicitSelectors
    .flatMap((selector) => Array.from(main.querySelectorAll(selector)))
    .find((candidate) => !isExcludedLegacyHeader(candidate));

  let candidate = explicit;
  if (!candidate) {
    const heading = Array.from(main.querySelectorAll('h1'))
      .find((element) => !isExcludedLegacyHeader(element));
    candidate = heading?.closest('header, [class*="page-header"], [class*="workspace-header"], [class*="module-header"]') || heading || null;
  }

  if (candidate && candidate !== main) {
    candidate.classList.add('pulse-enterprise-legacy-page-header');
    candidate.dataset.pulseLegacyHeaderRoute = route;
  }
}

function replaceVisibleText(element, nextText) {
  if (!element || cleanText(element.textContent) === nextText) return;
  element.textContent = nextText;
}

function applyCustomerNeutralModule006Presentation(main, route) {
  document.querySelectorAll([
    '[data-module-number="006"] h2',
    '[data-module-route="toyota-hyundai-pipelines"] h2',
    'a[href="#toyota-hyundai-pipelines"] .enterprise-nav-label',
    'a[href="#psa-modules"] .enterprise-nav-label'
  ].join(', ')).forEach((element) => replaceVisibleText(element, 'Customer Pipelines'));

  document.querySelectorAll([
    '[data-module-number="006"] > p',
    '[data-module-route="toyota-hyundai-pipelines"] > p'
  ].join(', ')).forEach((element) => replaceVisibleText(
    element,
    'Manage governed pipeline records, ownership, estimates, updates, documents, and action items for Toyota, Hyundai, and every other customer.'
  ));

  document.querySelectorAll([
    '[data-module-number="006"] .modules-directory-open-link',
    '[data-module-route="toyota-hyundai-pipelines"] .modules-directory-open-link'
  ].join(', ')).forEach((element) => {
    element.setAttribute('aria-label', 'Open Module 006 — Customer Pipelines');
  });

  if (route !== 'toyota-hyundai-pipelines' && route !== 'psa-modules') return;

  Array.from(main?.querySelectorAll('h1') || [])
    .filter((element) => !isExcludedLegacyHeader(element))
    .filter((element) => /toyota|hyundai|pipeline/i.test(cleanText(element.textContent)))
    .forEach((element) => replaceVisibleText(element, 'Customer Pipelines'));
}

function decorateRoute(main, route) {
  if (!main) return;
  main.dataset.pulseEnterpriseRoute = route;
  main.classList.add('pulse-enterprise-experience-enabled');
  decorateLegacyPageHeader(main, route);
  applyCustomerNeutralModule006Presentation(main, route);
}

function ViewIcon({ enterprise }) {
  return enterprise ? (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <rect x="3" y="3" width="7" height="7" rx="1.5" />
      <rect x="14" y="3" width="7" height="7" rx="1.5" />
      <rect x="3" y="14" width="7" height="7" rx="1.5" />
      <rect x="14" y="14" width="7" height="7" rx="1.5" />
    </svg>
  ) : (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="M4 6h16M4 12h16M4 18h16" />
    </svg>
  );
}

function WorkspaceIcon({ route }) {
  if (route === 'modules') {
    return (
      <svg viewBox="0 0 24 24" aria-hidden="true">
        <rect x="3" y="3" width="7" height="7" rx="1.5" />
        <rect x="14" y="3" width="7" height="7" rx="1.5" />
        <rect x="3" y="14" width="7" height="7" rx="1.5" />
        <rect x="14" y="14" width="7" height="7" rx="1.5" />
      </svg>
    );
  }

  if (route === 'toyota-hyundai-pipelines' || route === 'psa-modules') {
    return (
      <svg viewBox="0 0 24 24" aria-hidden="true">
        <path d="M4 7h6l2 3h8M4 17h6l2-3h8" />
        <circle cx="4" cy="7" r="2" />
        <circle cx="4" cy="17" r="2" />
        <circle cx="20" cy="10" r="2" />
        <circle cx="20" cy="14" r="2" />
      </svg>
    );
  }

  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="M12 3 4.5 6.5v5.8c0 4.4 2.9 7.4 7.5 8.7 4.6-1.3 7.5-4.3 7.5-8.7V6.5L12 3Z" />
      <path d="m9 12 2 2 4-4" />
    </svg>
  );
}

function ExperienceSwitcher({ experience, onChange }) {
  return (
    <div className="pulse-experience-switcher" role="group" aria-label="Interface view">
      <span className="pulse-experience-switcher__label">View</span>
      <button
        type="button"
        className={experience === ENTERPRISE_EXPERIENCE ? 'active' : ''}
        aria-pressed={experience === ENTERPRISE_EXPERIENCE}
        title="Use the enterprise interface"
        onClick={() => onChange(ENTERPRISE_EXPERIENCE)}
      >
        <ViewIcon enterprise />
        <strong>Enterprise</strong>
      </button>
      <button
        type="button"
        className={experience === CLASSIC_EXPERIENCE ? 'active' : ''}
        aria-pressed={experience === CLASSIC_EXPERIENCE}
        title="Use the classic interface"
        onClick={() => onChange(CLASSIC_EXPERIENCE)}
      >
        <ViewIcon enterprise={false} />
        <strong>Classic</strong>
      </button>
    </div>
  );
}

function EnterprisePageChrome({ metadata }) {
  return (
    <header className="pulse-enterprise-page-chrome" aria-label={`${metadata.title} page context`}>
      <div className="pulse-enterprise-page-chrome__identity">
        <span className="pulse-enterprise-page-chrome__icon" aria-hidden="true">
          <WorkspaceIcon route={metadata.route} />
        </span>
        <div>
          <p className="pulse-enterprise-page-chrome__eyebrow">
            {metadata.moduleNumber ? `Module ${metadata.moduleNumber}` : 'Pulse'}
            <i aria-hidden="true" />
            <span>{metadata.group}</span>
          </p>
          <h1>{metadata.title}</h1>
          <p className="pulse-enterprise-page-chrome__description">{metadata.description}</p>
        </div>
      </div>
      <div className="pulse-enterprise-page-chrome__status" aria-label="Presentation status">
        <span><i aria-hidden="true" /> Enterprise view</span>
        {metadata.moduleNumber ? <small>Module {metadata.moduleNumber}</small> : <small>Unified workspace</small>}
      </div>
    </header>
  );
}

export default function EnterpriseExperienceController() {
  const [experience, setExperience] = useState(() => readExperience());
  const [route, setRoute] = useState(() => currentRoute());
  const [controlHost, setControlHost] = useState(null);
  const [pageChromeHost, setPageChromeHost] = useState(null);
  const refreshFrame = useRef(0);

  const synchronize = useCallback(() => {
    window.cancelAnimationFrame(refreshFrame.current);
    refreshFrame.current = window.requestAnimationFrame(() => {
      const nextRoute = currentRoute();
      const main = document.querySelector('main.app-shell.enterprise-nav-enabled');
      const nextControlHost = ensureControlHost(main);
      const nextPageChromeHost = ensurePageChromeHost(main);

      applyExperience(experience, { announce: false });
      decorateRoute(main, nextRoute);

      setRoute((current) => current === nextRoute ? current : nextRoute);
      setControlHost((current) => current === nextControlHost ? current : nextControlHost);
      setPageChromeHost((current) => current === nextPageChromeHost ? current : nextPageChromeHost);
    });
  }, [experience]);

  useEffect(() => {
    applyExperience(experience, { announce: false });
    synchronize();

    const root = document.getElementById('root');
    const observer = root ? new MutationObserver(synchronize) : null;
    observer?.observe(root, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ['class', 'hidden', 'aria-expanded']
    });

    const handleRouteChange = () => synchronize();
    const handleStorage = (event) => {
      if (event.key && event.key !== EXPERIENCE_STORAGE_KEY) return;
      setExperience(readExperience());
    };
    const handleExternalExperience = (event) => {
      const next = normalizeExperience(event?.detail?.experience);
      setExperience((current) => current === next ? current : next);
    };

    window.addEventListener('hashchange', handleRouteChange);
    window.addEventListener('pageshow', handleRouteChange);
    window.addEventListener('projectpulse:view-as-changed', handleRouteChange);
    window.addEventListener('projectpulse:module-availability-changed', handleRouteChange);
    window.addEventListener('projectpulse:theme-changed', handleRouteChange);
    window.addEventListener('storage', handleStorage);
    window.addEventListener(EXPERIENCE_EVENT, handleExternalExperience);

    return () => {
      observer?.disconnect();
      window.cancelAnimationFrame(refreshFrame.current);
      window.removeEventListener('hashchange', handleRouteChange);
      window.removeEventListener('pageshow', handleRouteChange);
      window.removeEventListener('projectpulse:view-as-changed', handleRouteChange);
      window.removeEventListener('projectpulse:module-availability-changed', handleRouteChange);
      window.removeEventListener('projectpulse:theme-changed', handleRouteChange);
      window.removeEventListener('storage', handleStorage);
      window.removeEventListener(EXPERIENCE_EVENT, handleExternalExperience);
    };
  }, [experience, synchronize]);

  const metadata = useMemo(() => routeMetadata(route), [route]);

  const changeExperience = useCallback((nextExperience) => {
    const next = applyExperience(nextExperience);
    setExperience(next);
  }, []);

  return (
    <>
      {controlHost ? createPortal(
        <ExperienceSwitcher experience={experience} onChange={changeExperience} />,
        controlHost
      ) : null}
      {pageChromeHost ? createPortal(
        <EnterprisePageChrome metadata={metadata} />,
        pageChromeHost
      ) : null}
    </>
  );
}

export {
  CLASSIC_EXPERIENCE,
  ENTERPRISE_EXPERIENCE,
  EXPERIENCE_EVENT,
  EXPERIENCE_STORAGE_KEY,
  applyExperience,
  routeMetadata
};
