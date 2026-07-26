import { useEffect } from 'react';

const MODULE005_ROUTE = 'project-allocation-info';
const MODULE005_NAME = 'Project Expense Upload';

function currentRoute() {
  return String(window.location.hash || '').replace(/^#/, '').trim();
}

function renameModule005Navigation() {
  document.querySelectorAll('a[href="#project-allocation-info"]').forEach((link) => {
    const label = link.querySelector('.enterprise-nav-label');
    if (label) label.textContent = MODULE005_NAME;
    link.setAttribute('aria-label', `Open Module 005 ${MODULE005_NAME}`);
    link.setAttribute('title', MODULE005_NAME);
  });

  document.querySelectorAll('[data-route="project-allocation-info"], [data-module-route="project-allocation-info"]').forEach((item) => {
    const label = item.querySelector('[data-module-label], .module-title, .module-name');
    if (label) label.textContent = MODULE005_NAME;
  });
}

function prepareReupload() {
  const uploadTab = [...document.querySelectorAll('.expense-method-tabs button')]
    .find((button) => String(button.textContent || '').includes('Upload CSV / Excel'));
  uploadTab?.click();

  const panel = document.querySelector('.expense-selection-card');
  const input = panel?.querySelector('input[type="file"]');
  const status = document.querySelector('.expense-status');
  if (status) {
    status.textContent = 'Re-upload ready. Choose the replacement CSV or Excel file; the new upload will become the current version.';
  }
  panel?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  window.setTimeout(() => input?.focus(), 350);
}

function convertDeleteActionsToReupload() {
  document.querySelectorAll('.expense-history-card .expense-actions button').forEach((button) => {
    if (String(button.textContent || '').trim() !== 'Delete') return;
    if (button.dataset.projectpulseReupload === 'true') return;

    button.dataset.projectpulseReupload = 'true';
    button.textContent = 'Re-upload';
    button.setAttribute('aria-label', 'Re-upload a replacement expense file for this version');
    button.addEventListener('click', (event) => {
      event.preventDefault();
      event.stopPropagation();
      event.stopImmediatePropagation();
      prepareReupload();
    }, true);
  });
}

function synchronize() {
  renameModule005Navigation();
  if (currentRoute() === MODULE005_ROUTE) convertDeleteActionsToReupload();
}

export default function Module005ExperienceCompatibility() {
  useEffect(() => {
    let timer = 0;
    const schedule = () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(synchronize, 40);
    };

    synchronize();
    const observer = new MutationObserver(schedule);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', schedule);

    return () => {
      window.clearTimeout(timer);
      observer.disconnect();
      window.removeEventListener('hashchange', schedule);
    };
  }, []);

  return null;
}
