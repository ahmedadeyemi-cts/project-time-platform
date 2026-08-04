import { useEffect } from 'react';

const MODULE005_NAME = 'Project Expense Upload';

function setAttributeWhenChanged(element, name, value) {
  if (element.getAttribute(name) !== value) element.setAttribute(name, value);
}

function setTextWhenChanged(element, value) {
  if (element && String(element.textContent || '') !== value) element.textContent = value;
}

function synchronizeModule005Identity() {
  document.querySelectorAll('a[href="#project-allocation-info"]').forEach((link) => {
    setTextWhenChanged(link.querySelector('.enterprise-nav-label'), MODULE005_NAME);
    setAttributeWhenChanged(link, 'aria-label', `Open Module 005 ${MODULE005_NAME}`);
    setAttributeWhenChanged(link, 'title', MODULE005_NAME);
  });
}

export default function Module005ExperienceCompatibility() {
  useEffect(() => {
    synchronizeModule005Identity();
    const observer = new MutationObserver(synchronizeModule005Identity);
    observer.observe(document.body, { childList: true, subtree: true });
    return () => observer.disconnect();
  }, []);
  return null;
}
