const GOVERNED_MODULE_PATHS = Object.freeze([
  ['/api/project-risk-register', '082'],
  ['/api/lab-equipment-tracker', '081']
]);

const LEGACY_DISPLAY_LABELS = Object.freeze([
  new RegExp(`(?<![\\w-])${['Project', 'Pulse'].join('')}(?![\\w-])`, 'g'),
  new RegExp(`(?<![\\w-])${['Project', 'Pulse'].join(' ')}(?![\\w-])`, 'g'),
  new RegExp(`(?<![\\w-])${['Project', 'Health', 'Platform'].join(' ')}(?![\\w-])`, 'gi')
]);

const DISPLAY_ATTRIBUTES = Object.freeze(['aria-label', 'title', 'placeholder', 'alt']);
const NON_PRESENTATION_SELECTOR = 'script, style, noscript, code, pre, textarea, input, [contenteditable="true"]';
const THEME_STORAGE_KEY = 'ptp-theme';
const THEME_EVENT = 'projectpulse:theme-changed';
const THEME_SWITCHER_ATTRIBUTE = 'data-pulse-header-theme-switcher';

function sameOriginApiPath(input) {
  try {
    const raw = typeof input === 'string' ? input : input?.url;
    if (!raw) return '';
    const url = new URL(raw, window.location.origin);
    return url.origin === window.location.origin ? url.pathname : '';
  } catch {
    return '';
  }
}

function moduleNumberForPath(path) {
  const normalized = String(path || '').toLowerCase();
  const match = GOVERNED_MODULE_PATHS.find(([prefix]) => (
    normalized === prefix || normalized.startsWith(`${prefix}/`)
  ));
  return match?.[1] || '';
}

function installGovernedModuleFetchHeaders() {
  if (typeof window.fetch !== 'function' || window.__pulseGovernedModuleFetchHeadersInstalled) return;

  const previousFetch = window.fetch.bind(window);
  window.fetch = (input, init = {}) => {
    const moduleNumber = moduleNumberForPath(sameOriginApiPath(input));
    if (!moduleNumber) return previousFetch(input, init);

    const inheritedHeaders = input instanceof Request ? input.headers : undefined;
    const headers = new Headers(init.headers || inheritedHeaders);
    headers.set('X-ProjectPulse-Module-Number', moduleNumber);
    if (!headers.has('Accept')) headers.set('Accept', 'application/json');
    headers.set('Cache-Control', 'no-cache, no-store, max-age=0');
    headers.set('Pragma', 'no-cache');

    return previousFetch(input, {
      ...init,
      headers,
      credentials: init.credentials || 'include',
      cache: 'no-store'
    });
  };

  window.__pulseGovernedModuleFetchHeadersInstalled = true;
}

function normalizeDisplayValue(value) {
  let normalized = String(value ?? '');
  for (const pattern of LEGACY_DISPLAY_LABELS) {
    normalized = normalized.replace(pattern, 'Pulse');
  }
  return normalized;
}

function isPresentationTextNode(node) {
  const parent = node?.parentElement;
  return Boolean(parent && !parent.closest(NON_PRESENTATION_SELECTOR));
}

function normalizeTextNode(node) {
  if (!isPresentationTextNode(node)) return;
  const normalized = normalizeDisplayValue(node.nodeValue);
  if (normalized !== node.nodeValue) node.nodeValue = normalized;
}

function normalizeElementAttributes(element) {
  if (!(element instanceof Element) || element.matches(NON_PRESENTATION_SELECTOR)) return;
  for (const attribute of DISPLAY_ATTRIBUTES) {
    if (!element.hasAttribute(attribute)) continue;
    const current = element.getAttribute(attribute) || '';
    const normalized = normalizeDisplayValue(current);
    if (normalized !== current) element.setAttribute(attribute, normalized);
  }
}

function normalizeFrontendDisplay(root = document.body) {
  if (!root) return;

  if (root.nodeType === Node.TEXT_NODE) {
    normalizeTextNode(root);
    return;
  }

  if (!(root instanceof Element) && root !== document) return;
  if (root instanceof Element) normalizeElementAttributes(root);

  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  const textNodes = [];
  while (walker.nextNode()) textNodes.push(walker.currentNode);
  textNodes.forEach(normalizeTextNode);

  if (root.querySelectorAll) {
    root.querySelectorAll(DISPLAY_ATTRIBUTES.map((name) => `[${name}]`).join(','))
      .forEach(normalizeElementAttributes);
  }

  const normalizedTitle = normalizeDisplayValue(document.title);
  if (normalizedTitle !== document.title) document.title = normalizedTitle;
}

function currentTheme() {
  const stored = (() => {
    try { return window.localStorage.getItem(THEME_STORAGE_KEY); } catch { return ''; }
  })();
  const declared = document.documentElement.dataset.theme || document.body?.dataset.theme || stored || 'light';
  return String(declared).toLowerCase() === 'dark' ? 'dark' : 'light';
}

function synchronizeThemeButtons() {
  const theme = currentTheme();
  document.querySelectorAll('[data-pulse-theme-choice]').forEach((button) => {
    const active = button.dataset.pulseThemeChoice === theme;
    button.setAttribute('aria-pressed', active ? 'true' : 'false');
    button.classList.toggle('active', active);
  });
}

function applyTheme(theme) {
  const normalized = theme === 'dark' ? 'dark' : 'light';
  try { window.localStorage.setItem(THEME_STORAGE_KEY, normalized); } catch { /* Storage can be unavailable. */ }
  document.documentElement.dataset.theme = normalized;
  if (document.body) document.body.dataset.theme = normalized;
  window.dispatchEvent(new CustomEvent(THEME_EVENT, { detail: { theme: normalized } }));
  synchronizeThemeButtons();
}

function themeButton(theme, icon, label) {
  const button = document.createElement('button');
  button.type = 'button';
  button.dataset.pulseThemeChoice = theme;
  button.setAttribute('aria-label', `Use ${label.toLowerCase()} appearance`);
  button.innerHTML = `<span aria-hidden="true">${icon}</span><strong>${label}</strong>`;
  return button;
}

function ensureHeaderThemeSwitcher() {
  const utilities = document.querySelector('.enterprise-top-bar .enterprise-header-utilities');
  if (!utilities) return null;

  let switcher = utilities.querySelector(`[${THEME_SWITCHER_ATTRIBUTE}]`);
  if (!switcher) {
    switcher = document.createElement('div');
    switcher.className = 'pulse-header-theme-switcher';
    switcher.setAttribute(THEME_SWITCHER_ATTRIBUTE, 'true');
    switcher.setAttribute('role', 'group');
    switcher.setAttribute('aria-label', 'Appearance');
    switcher.append(
      themeButton('light', '☀', 'Light'),
      themeButton('dark', '☾', 'Dark')
    );

    const profileMenu = utilities.querySelector('.profile-menu-shell');
    utilities.insertBefore(switcher, profileMenu || utilities.firstChild);
  }

  synchronizeThemeButtons();
  return switcher;
}

function installPresentationRuntime() {
  if (window.__pulseShellFrontendCompatibilityInstalled) return;
  window.__pulseShellFrontendCompatibilityInstalled = true;

  installGovernedModuleFetchHeaders();

  const refreshPresentation = (root = document.body) => {
    normalizeFrontendDisplay(root);
    ensureHeaderThemeSwitcher();
    synchronizeThemeButtons();
  };

  document.addEventListener('click', (event) => {
    const button = event.target?.closest?.('[data-pulse-theme-choice]');
    if (!button) return;
    event.preventDefault();
    applyTheme(button.dataset.pulseThemeChoice);
  }, true);

  const start = () => {
    applyTheme(currentTheme());
    refreshPresentation();

    const observer = new MutationObserver((mutations) => {
      for (const mutation of mutations) {
        if (mutation.type === 'characterData') {
          normalizeTextNode(mutation.target);
          continue;
        }
        mutation.addedNodes.forEach((node) => refreshPresentation(node));
      }
      ensureHeaderThemeSwitcher();
    });

    observer.observe(document.body, {
      childList: true,
      subtree: true,
      characterData: true
    });
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', start, { once: true });
  } else {
    start();
  }

  window.addEventListener('hashchange', () => window.requestAnimationFrame(() => refreshPresentation()));
  window.addEventListener('pageshow', () => window.requestAnimationFrame(() => refreshPresentation()));
  window.addEventListener(THEME_EVENT, synchronizeThemeButtons);
  window.addEventListener('storage', (event) => {
    if (!event.key || event.key === THEME_STORAGE_KEY) {
      applyTheme(event.newValue === 'dark' ? 'dark' : 'light');
    }
  });
}

if (typeof window !== 'undefined' && typeof document !== 'undefined') {
  installPresentationRuntime();
}
