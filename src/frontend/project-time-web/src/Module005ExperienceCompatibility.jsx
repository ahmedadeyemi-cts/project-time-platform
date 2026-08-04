import { useEffect } from 'react';

const MODULE005_NAME = 'Project Expense Upload';
const MODULE005_FULL_NAME = 'Project Expense Upload — Module 005';

function currentRoute() {
  return String(window.location.hash || '').replace(/^#/, '').split('?')[0].trim();
}

function setAttributeWhenChanged(element, name, value) {
  if (element && element.getAttribute(name) !== value) element.setAttribute(name, value);
}

function setTextWhenChanged(element, value) {
  if (element && String(element.textContent || '').trim() !== value) element.textContent = value;
}

function shouldReplaceLegacyText(value) {
  return /project\s+allocation(?:\s*(?:\/|&|and)\s*)?(?:info|information)?/i.test(String(value || ''))
    || String(value || '').trim().toLowerCase() === 'project-allocation-info';
}

function synchronizeModule005Identity() {
  document.querySelectorAll('a[href="#project-allocation-info"]').forEach((link) => {
    setTextWhenChanged(link.querySelector('.enterprise-nav-label'), MODULE005_NAME);
    setAttributeWhenChanged(link, 'aria-label', `Open Module 005 ${MODULE005_NAME}`);
    setAttributeWhenChanged(link, 'title', MODULE005_NAME);
  });

  if (currentRoute() !== 'project-allocation-info') return;

  const workspaceHeading = document.querySelector('.workspace-header-context h1, .workspace-header-context strong');
  if (workspaceHeading && shouldReplaceLegacyText(workspaceHeading.textContent)) {
    setTextWhenChanged(workspaceHeading, MODULE005_NAME);
  }

  document.querySelectorAll('.page-context-guide summary strong, .page-context-guide summary h1, .page-context-guide summary h2')
    .forEach((element) => {
      if (shouldReplaceLegacyText(element.textContent)) setTextWhenChanged(element, MODULE005_FULL_NAME);
    });

  document.querySelectorAll('[data-page-name], [aria-label], [title]').forEach((element) => {
    const pageName = element.getAttribute('data-page-name');
    if (shouldReplaceLegacyText(pageName)) element.setAttribute('data-page-name', MODULE005_NAME);
    const aria = element.getAttribute('aria-label');
    if (shouldReplaceLegacyText(aria)) element.setAttribute('aria-label', aria.replace(/project\s+allocation(?:\s*(?:\/|&|and)\s*)?(?:info|information)?/ig, MODULE005_NAME));
    const title = element.getAttribute('title');
    if (shouldReplaceLegacyText(title)) element.setAttribute('title', title.replace(/project\s+allocation(?:\s*(?:\/|&|and)\s*)?(?:info|information)?/ig, MODULE005_NAME));
  });
}

export default function Module005ExperienceCompatibility() {
  useEffect(() => {
    synchronizeModule005Identity();
    const observer = new MutationObserver(synchronizeModule005Identity);
    observer.observe(document.body, { childList: true, subtree: true, characterData: true });
    window.addEventListener('hashchange', synchronizeModule005Identity);
    return () => {
      observer.disconnect();
      window.removeEventListener('hashchange', synchronizeModule005Identity);
    };
  }, []);
  return null;
}
