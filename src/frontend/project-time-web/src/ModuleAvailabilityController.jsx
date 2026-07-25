import { useCallback, useEffect, useRef, useState } from 'react';
import {
  PROJECTPULSE_MODULES,
  currentProjectPulseRoute,
  moduleForRoute,
  replaceTimesheetLabel
} from './module-availability-registry.js';
import './module-availability.css';

const REFRESH_INTERVAL_MS = 30000;

async function readJson(response) {
  const raw = await response.text();
  if (!raw.trim()) return {};
  try {
    return JSON.parse(raw);
  } catch {
    return { message: raw };
  }
}

function messageFrom(payload, fallback) {
  return payload?.message || payload?.status || fallback;
}

function normalizeOverrideResponse(body) {
  if (!Array.isArray(body?.states)) {
    throw new Error('Module availability returned an invalid override response.');
  }

  const states = new Map();
  for (const state of body.states) {
    const moduleNumber = String(state?.moduleNumber || '').trim().toUpperCase();
    if (!moduleNumber) continue;
    states.set(moduleNumber, {
      isEnabled: state?.isEnabled !== false,
      revision: Number(state?.revision || 0),
      reason: String(state?.reason || '').trim()
    });
  }

  return {
    loaded: true,
    states,
    access: body?.access || {},
    error: ''
  };
}

function normalizeTimesheetLabels(root = document) {
  const targets = root.querySelectorAll?.('a[href="#timesheet"], [data-route="timesheet"], [data-module-route="timesheet"]') || [];
  for (const target of targets) {
    const walker = document.createTreeWalker(target, NodeFilter.SHOW_TEXT);
    const textNodes = [];
    while (walker.nextNode()) textNodes.push(walker.currentNode);
    for (const textNode of textNodes) {
      const next = replaceTimesheetLabel(textNode.nodeValue);
      if (next !== textNode.nodeValue) textNode.nodeValue = next;
    }
    target.setAttribute('aria-label', replaceTimesheetLabel(target.getAttribute('aria-label') || ''));
    target.setAttribute('title', replaceTimesheetLabel(target.getAttribute('title') || ''));
  }
}

function selectorsForModule(module) {
  const selectors = [`a[href="#${module.route}"]`];
  if (module.route === 'project-workload') {
    selectors.push('a[href="#project-manager-workload"]', 'a[href="#project-management-workload"]');
  }
  if (module.route === 'signed-handoff') selectors.push('a[href="#resource-assignment-handoff"]');
  return selectors.join(',');
}

function clearAvailabilityNavigationState() {
  for (const element of document.querySelectorAll('[data-module-availability-hidden="true"]')) {
    element.hidden = false;
    delete element.dataset.moduleAvailabilityHidden;
  }
  for (const element of document.querySelectorAll('.projectpulse-module-disabled')) {
    element.classList.remove('projectpulse-module-disabled');
    delete element.dataset.moduleAvailabilityStatus;
  }
  normalizeTimesheetLabels();
}

function applyModuleNavigationState(states, isSuperAdministrator) {
  for (const module of PROJECTPULSE_MODULES) {
    const stored = states.get(module.moduleNumber);
    const isEnabled = stored?.isEnabled !== false;

    for (const element of document.querySelectorAll(selectorsForModule(module))) {
      const hiddenForAvailability = !isEnabled && !isSuperAdministrator;
      if (hiddenForAvailability) {
        element.hidden = true;
        element.dataset.moduleAvailabilityHidden = 'true';
      } else if (element.dataset.moduleAvailabilityHidden === 'true') {
        element.hidden = false;
        delete element.dataset.moduleAvailabilityHidden;
      }

      element.classList.toggle('projectpulse-module-disabled', !isEnabled && isSuperAdministrator);
      if (!isEnabled && isSuperAdministrator) {
        element.dataset.moduleAvailabilityStatus = 'Disabled';
      } else {
        delete element.dataset.moduleAvailabilityStatus;
      }
    }
  }

  normalizeTimesheetLabels();
}

function disabledCurrentModule(states, isSuperAdministrator) {
  if (!isSuperAdministrator) return null;
  const current = moduleForRoute(currentProjectPulseRoute());
  if (!current) return null;
  return states.get(current.moduleNumber)?.isEnabled === false ? current : null;
}

export default function ModuleAvailabilityController() {
  const [availability, setAvailability] = useState({
    loaded: false,
    states: new Map(),
    access: {},
    error: ''
  });
  const [statusMessage, setStatusMessage] = useState('');
  const refreshTimer = useRef(null);

  const load = useCallback(async () => {
    try {
      const response = await fetch('/api/module-availability/overrides', { cache: 'no-store' });
      const body = await readJson(response);
      if (!response.ok) throw new Error(messageFrom(body, 'Module availability controls could not be loaded.'));
      const normalized = normalizeOverrideResponse(body);
      setAvailability(normalized);
      window.__projectPulseModuleAvailabilityOverrides = normalized;
      window.dispatchEvent(new CustomEvent('projectpulse:module-availability-loaded', { detail: normalized }));
    } catch (error) {
      const failed = {
        loaded: false,
        states: new Map(),
        access: {},
        error: error?.message || 'Module availability controls could not be loaded.'
      };
      setAvailability(failed);
      window.__projectPulseModuleAvailabilityOverrides = failed;
      clearAvailabilityNavigationState();
    }
  }, []);

  useEffect(() => {
    void load();
    const interval = window.setInterval(() => void load(), REFRESH_INTERVAL_MS);
    const refresh = () => void load();
    window.addEventListener('projectpulse:module-availability-changed', refresh);
    window.addEventListener('projectpulse:view-as-changed', refresh);
    return () => {
      window.clearInterval(interval);
      window.removeEventListener('projectpulse:module-availability-changed', refresh);
      window.removeEventListener('projectpulse:view-as-changed', refresh);
    };
  }, [load]);

  useEffect(() => {
    if (!availability.loaded) {
      clearAvailabilityNavigationState();
      return undefined;
    }

    const isSuperAdministrator = Boolean(availability.access?.isSuperAdministrator);
    const apply = () => {
      applyModuleNavigationState(availability.states, isSuperAdministrator);

      const current = moduleForRoute(currentProjectPulseRoute());
      if (!current) return;
      const isEnabled = availability.states.get(current.moduleNumber)?.isEnabled !== false;
      if (!isEnabled && !isSuperAdministrator) {
        setStatusMessage(`${current.displayName} is disabled. You were returned to the Modules directory.`);
        window.location.hash = 'modules';
      }
    };

    apply();
    const observer = new MutationObserver(() => {
      window.clearTimeout(refreshTimer.current);
      refreshTimer.current = window.setTimeout(apply, 40);
    });
    observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'hidden'] });
    window.addEventListener('hashchange', apply);
    return () => {
      observer.disconnect();
      window.removeEventListener('hashchange', apply);
      window.clearTimeout(refreshTimer.current);
    };
  }, [availability]);

  const disabledCurrent = availability.loaded
    ? disabledCurrentModule(availability.states, Boolean(availability.access?.isSuperAdministrator))
    : null;

  return (
    <>
      {disabledCurrent ? (
        <div className="module-availability-super-banner">
          Module {disabledCurrent.moduleNumber} — {disabledCurrent.displayName} is disabled.
          It remains visible because you are a Super Administrator.
        </div>
      ) : null}
      {statusMessage ? <div className="module-availability-toast success">{statusMessage}</div> : null}
    </>
  );
}
