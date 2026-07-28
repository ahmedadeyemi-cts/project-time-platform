const THEME_CONTROL_SELECTOR = '[data-projectpulse-theme-control="true"]';
const THEME_CONTROL_ID = 'projectpulse-floating-theme-toggle';
const STRAY_THEME_TEXT = /^(?:\\n|\/n|n)$/i;

function cleanText(value) {
  return String(value || '').replace(/\s+/g, ' ').trim();
}

function isThemeControl(button) {
  if (!(button instanceof HTMLButtonElement)) return false;
  const text = cleanText(button.textContent);
  const accessibleName = cleanText([
    button.getAttribute('aria-label'),
    button.getAttribute('title')
  ].filter(Boolean).join(' '));
  return button.id === THEME_CONTROL_ID
    || /^(?:🌙|☀️?|◐)?\s*(?:dark|light)\s+mode$/i.test(text)
    || /switch\s+to\s+(?:dark|light)\s+mode/i.test(accessibleName)
    || button.matches('.theme-toggle, [data-theme-toggle], [data-theme-control]');
}

function findThemeControl() {
  return document.querySelector(THEME_CONTROL_SELECTOR)
    || document.getElementById(THEME_CONTROL_ID)
    || [...document.querySelectorAll('button')].find(isThemeControl)
    || null;
}

function currentTheme() {
  const declared = document.documentElement.dataset.theme
    || document.body?.dataset.theme
    || window.localStorage.getItem('ptp-theme')
    || 'light';
  return String(declared).toLowerCase() === 'dark' ? 'dark' : 'light';
}

function applyTheme(theme) {
  const normalized = theme === 'dark' ? 'dark' : 'light';
  try { window.localStorage.setItem('ptp-theme', normalized); } catch { /* browser storage unavailable */ }
  document.documentElement.dataset.theme = normalized;
  if (document.body) document.body.dataset.theme = normalized;
  window.dispatchEvent(new CustomEvent('projectpulse:theme-changed', { detail: { theme: normalized } }));
}

function neutralizeStrayThemeText(button) {
  if (!button || button.parentNode !== document.body) return;
  for (const node of [...document.body.childNodes]) {
    if (node.nodeType !== Node.TEXT_NODE) continue;
    const value = String(node.textContent || '').trim().replace(/\u00a0/g, ' ').trim();
    if (STRAY_THEME_TEXT.test(value)) node.textContent = '';
  }
}

function polishThemeControl() {
  const button = findThemeControl();
  if (!button) return false;

  const theme = currentTheme();
  const target = theme === 'dark' ? 'light' : 'dark';
  neutralizeStrayThemeText(button);

  button.classList.add('projectpulse-theme-control');
  button.dataset.projectpulseThemeControl = 'true';
  button.dataset.projectpulseTheme = theme;
  button.type = 'button';
  button.textContent = '';
  button.setAttribute('aria-label', `Switch to ${target} mode`);
  button.setAttribute('title', `Switch to ${target} mode`);
  button.setAttribute('aria-pressed', theme === 'dark' ? 'true' : 'false');
  return true;
}

function schedulePolish() {
  window.requestAnimationFrame(() => polishThemeControl());
  window.setTimeout(polishThemeControl, 80);
  window.setTimeout(polishThemeControl, 350);
}

function handleThemeClick(event) {
  const button = event.target?.closest?.('button');
  if (!isThemeControl(button)) return;
  event.preventDefault();
  event.stopImmediatePropagation();
  const next = currentTheme() === 'dark' ? 'light' : 'dark';
  applyTheme(next);
  schedulePolish();
}

function installThemeControlPolish() {
  if (window.__projectPulseThemeControlPolishInstalled) return;
  window.__projectPulseThemeControlPolishInstalled = true;

  document.addEventListener('click', handleThemeClick, true);
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', schedulePolish, { once: true });
  } else {
    schedulePolish();
  }

  window.addEventListener('hashchange', schedulePolish);
  window.addEventListener('pageshow', schedulePolish);
  window.addEventListener('projectpulse:theme-changed', schedulePolish);
  window.addEventListener('storage', (event) => {
    if (!event.key || event.key === 'ptp-theme') schedulePolish();
  });
}

if (typeof window !== 'undefined' && typeof document !== 'undefined') {
  installThemeControlPolish();
}
