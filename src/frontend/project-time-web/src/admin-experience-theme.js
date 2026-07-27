function removeStrayThemeText(button) {
  const parent = button?.parentNode;
  if (!parent) return;

  [...parent.childNodes].forEach((node) => {
    if (node.nodeType !== Node.TEXT_NODE) return;
    const value = String(node.textContent || '').trim();
    if (/^(?:\\n|\/n|n)$/i.test(value)) node.remove();
  });
}

function polishThemeControl() {
  const button = document.querySelector('.theme-toggle');
  if (!button) return;

  removeStrayThemeText(button);
  button.classList.add('projectpulse-theme-control');
  button.dataset.projectpulseThemeControl = 'true';

  const dark = document.documentElement.dataset.theme === 'dark';
  button.setAttribute('aria-label', dark ? 'Switch to light mode' : 'Switch to dark mode');
  button.setAttribute('title', dark ? 'Switch to light mode' : 'Switch to dark mode');
}

function installThemeControlPolish() {
  if (window.__projectPulseThemeControlPolishInstalled) return;
  window.__projectPulseThemeControlPolishInstalled = true;

  const run = () => window.requestAnimationFrame(polishThemeControl);
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', run, { once: true });
  } else {
    run();
  }

  window.addEventListener('hashchange', run);
  window.addEventListener('storage', run);

  const observer = new MutationObserver(run);
  observer.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ['data-theme'],
    childList: true,
    subtree: true
  });

  window.__projectPulseThemeControlPolishObserver = observer;
}

if (typeof window !== 'undefined' && typeof document !== 'undefined') {
  installThemeControlPolish();
}
