const THEME_CONTROL_SELECTOR = '[data-projectpulse-theme-control="true"]';
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

  return /^(?:🌙|☀️?|◐)?\s*(?:dark|light)\s+mode$/i.test(text)
    || /switch\s+to\s+(?:dark|light)\s+mode/i.test(accessibleName)
    || button.matches('.theme-toggle, [data-theme-toggle], [data-theme-control]');
}

function findThemeControl() {
  return document.querySelector(THEME_CONTROL_SELECTOR)
    || [...document.querySelectorAll('button')].find(isThemeControl)
    || null;
}

function removeStrayThemeText(button) {
  const containers = [
    button?.parentNode,
    button?.parentElement?.parentNode,
    document.body
  ].filter(Boolean);

  for (const container of containers) {
    [...container.childNodes].forEach((node) => {
      if (node.nodeType !== Node.TEXT_NODE) return;
      const value = String(node.textContent || '').trim().replace(/\u00a0/g, ' ').trim();
      if (STRAY_THEME_TEXT.test(value)) node.remove();
    });
  }

  const previous = button?.previousSibling;
  if (previous?.nodeType === Node.TEXT_NODE) {
    const value = String(previous.textContent || '').trim().replace(/\u00a0/g, ' ').trim();
    if (STRAY_THEME_TEXT.test(value)) previous.remove();
  }
}

function currentTheme() {
  const declared = document.documentElement.dataset.theme
    || document.body?.dataset.theme
    || window.localStorage.getItem('ptp-theme')
    || 'light';

  return String(declared).toLowerCase() === 'dark' ? 'dark' : 'light';
}

function polishThemeControl() {
  const button = findThemeControl();
  if (!button) return;

  removeStrayThemeText(button);

  const theme = currentTheme();
  const target = theme === 'dark' ? 'light' : 'dark';

  button.classList.add('projectpulse-theme-control');
  button.dataset.projectpulseThemeControl = 'true';
  button.dataset.projectpulseTheme = theme;
  button.type = 'button';
  button.setAttribute('aria-label', `Switch to ${target} mode`);
  button.setAttribute('title', `Switch to ${target} mode`);
  button.setAttribute('aria-pressed', theme === 'dark' ? 'true' : 'false');
}

function installThemeControlPolish() {
  if (window.__projectPulseThemeControlPolishInstalled) return;
  window.__projectPulseThemeControlPolishInstalled = true;

  let scheduled = false;
  const run = () => {
    if (scheduled) return;
    scheduled = true;
    window.requestAnimationFrame(() => {
      scheduled = false;
      polishThemeControl();
    });
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', run, { once: true });
  } else {
    run();
  }

  window.addEventListener('hashchange', run);
  window.addEventListener('pageshow', run);
  window.addEventListener('storage', (event) => {
    if (!event.key || event.key === 'ptp-theme') run();
  });

  const observer = new MutationObserver(run);
  observer.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ['data-theme'],
    childList: true,
    subtree: true,
    characterData: true
  });

  window.__projectPulseThemeControlPolishObserver = observer;
}

if (typeof window !== 'undefined' && typeof document !== 'undefined') {
  installThemeControlPolish();
}
