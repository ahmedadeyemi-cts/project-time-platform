import './intuitive-more-menu.css';

const MARKER = '__projectPulseIntuitiveMoreMenuInstalled';
const REFINED = 'data-projectpulse-intuitive-more-link';

if (typeof window !== 'undefined' && typeof document !== 'undefined' && !window[MARKER]) {
  window[MARKER] = true;
  let timer = 0;
  let observer = null;

  function schedule() {
    window.clearTimeout(timer);
    timer = window.setTimeout(refine, 35);
  }

  function pageName(link) {
    const decorated = link.querySelector('.projectpulse-more-link-copy strong');
    if (decorated?.textContent?.trim()) return decorated.textContent.trim();
    const direct = link.querySelector('strong');
    if (direct?.textContent?.trim()) return direct.textContent.trim();
    return String(link.textContent || '').replace(/MODULE\s+[0-9A-Z]+/gi, '').trim();
  }

  function refineTools(dropdown) {
    const tools = dropdown.querySelector(':scope > .projectpulse-more-menu-tools');
    if (!tools) return;

    let heading = tools.querySelector('.projectpulse-more-intuitive-heading');
    if (!heading) {
      heading = document.createElement('div');
      heading.className = 'projectpulse-more-intuitive-heading';
      heading.innerHTML = '<strong>More pages</strong><span>Open another page available to your current role.</span>';
      tools.prepend(heading);
    }

    const label = tools.querySelector('label[for="projectpulse-more-menu-search"]');
    if (label) label.textContent = 'Search pages';
    const input = tools.querySelector('#projectpulse-more-menu-search');
    if (input) {
      input.placeholder = 'Search by page name';
      input.setAttribute('aria-label', 'Search available pages by name');
    }
    const clear = tools.querySelector('.projectpulse-more-menu-search-row button');
    if (clear) {
      clear.textContent = 'Clear';
      clear.title = 'Clear page search';
    }
  }

  function refineLink(link) {
    const name = pageName(link);
    if (!name) return;
    if (link.getAttribute(REFINED) === name && link.querySelector('.projectpulse-more-intuitive-name')) return;

    const title = document.createElement('strong');
    title.className = 'projectpulse-more-intuitive-name';
    title.textContent = name;
    const arrow = document.createElement('span');
    arrow.className = 'projectpulse-more-intuitive-arrow';
    arrow.setAttribute('aria-hidden', 'true');
    arrow.textContent = '›';

    link.replaceChildren(title, arrow);
    link.setAttribute(REFINED, name);
    link.setAttribute('aria-label', `Open ${name}`);
    link.title = name;
  }

  function refine() {
    const dropdown = document.querySelector('#enterprise-more-navigation-menu.enterprise-more-dropdown');
    if (!dropdown) return;
    dropdown.classList.add('projectpulse-more-intuitive');
    refineTools(dropdown);
    dropdown.querySelectorAll('.enterprise-more-links > a[href]').forEach(refineLink);
  }

  function boot() {
    refine();
    observer = new MutationObserver((mutations) => {
      if (!mutations.some((mutation) => mutation.addedNodes.length || mutation.type === 'attributes')) return;
      schedule();
    });
    observer.observe(document.body, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ['class', 'hidden', 'data-projectpulse-more-decoration']
    });
    window.addEventListener('hashchange', schedule);
    window.addEventListener('projectpulse:permission-navigation-updated', schedule);
    window.addEventListener('projectpulse:module-availability-changed', schedule);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot, { once: true });
  else boot();
}
