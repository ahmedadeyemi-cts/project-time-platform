import { useEffect, useRef } from 'react';
import { authoritativeApi } from '../projectpulse-authoritative-api.js';
import TimesheetEnhancementPortalV2 from './TimesheetEnhancementPortalV2.jsx';

const AUGMENTED_MARKER = '__module001AuthoritativeTimerTargets';

function targetKey(target = {}) {
  const selectionValue = String(target.selectionValue || '').trim();
  if (selectionValue) return selectionValue;

  const assignmentId = String(target.assignmentId || target.projectAssignmentId || target.targetId || '').trim();
  if (assignmentId && (target.targetType === 'assignment' || target.taskId || target.projectId)) return `assignment:${assignmentId}`;

  const categoryId = String(
    target.nonProjectTimeCategoryId
    || target.nonProjectCategoryId
    || target.categoryId
    || target.id
    || (target.targetType === 'category' ? target.targetId : '')
    || ''
  ).trim();
  if (categoryId) return `category:${categoryId}`;

  const categoryCode = String(target.categoryCode || target.code || target.targetCode || '').trim().toUpperCase();
  if (categoryCode) return `category-code:${categoryCode}`;

  const projectId = String(target.projectId || '').trim();
  const taskId = String(target.taskId || '').trim();
  if (projectId || taskId) return `task:${projectId}:${taskId}`;

  const label = String(target.categoryName || target.name || target.taskName || target.selectionLabel || '').trim().toLowerCase();
  return label ? `label:${label}` : '';
}

function mergeByKey(existing, incoming) {
  const rows = new Map();
  for (const item of existing || []) {
    const key = targetKey(item);
    if (key) rows.set(key, item);
  }
  for (const item of incoming || []) {
    const key = targetKey(item);
    if (key) rows.set(key, { ...(rows.get(key) || {}), ...item });
  }
  return [...rows.values()];
}

function synchronizeViewButtons() {
  const page = document.querySelector('#timesheet');
  if (!page?.classList.contains('module001-timer-mode')) return;

  const timerButton = page.querySelector('#module001-start-stop-tab');
  if (!timerButton) return;

  page.querySelectorAll('.timesheet-view-button').forEach((button) => {
    const active = button === timerButton;
    button.classList.toggle('active', active);
    button.setAttribute('aria-selected', active ? 'true' : 'false');
  });
}

export default function TimesheetEnhancementPortal() {
  const cacheRef = useRef(new Map());
  const pendingRef = useRef(new Set());
  const latestSnapshotRef = useRef(new Map());

  useEffect(() => {
    let timer = 0;
    const schedule = () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(synchronizeViewButtons, 40);
    };

    schedule();
    document.addEventListener('click', schedule, true);
    window.addEventListener('hashchange', schedule);
    const observer = new MutationObserver(schedule);
    observer.observe(document.body, { childList: true, subtree: true });

    return () => {
      window.clearTimeout(timer);
      observer.disconnect();
      document.removeEventListener('click', schedule, true);
      window.removeEventListener('hashchange', schedule);
    };
  }, []);

  useEffect(() => {
    let disposed = false;

    const publish = (snapshot, payload, error = '') => {
      if (disposed || !snapshot?.selectedWeekStart) return;
      const targets = Array.isArray(payload?.targets) ? payload.targets : [];
      const authoritativeAssignments = targets.filter((target) => target.targetType === 'assignment');
      const nonProjectTargets = targets.filter((target) => target.targetType === 'category' || target.targetType === 'categoryCode');
      const assignedTasks = mergeByKey(snapshot.assignedTasks, authoritativeAssignments);
      const nonProjectCategories = mergeByKey(
        snapshot.nonProjectCategories,
        nonProjectTargets.map((target) => ({
          ...target,
          id: target.nonProjectTimeCategoryId || target.targetId || target.id,
          nonProjectTimeCategoryId: target.nonProjectTimeCategoryId || target.targetId || target.id,
          code: target.categoryCode || target.targetCode || target.code,
          categoryCode: target.categoryCode || target.targetCode || target.code,
          name: target.categoryName || target.selectionLabel || target.name,
          categoryName: target.categoryName || target.selectionLabel || target.name
        }))
      );
      const enrichedSnapshot = {
        ...snapshot,
        assignedTasks,
        regularAssignedTasks: assignedTasks.filter((target) => target.groupLabel !== 'Service Request Tasks' && target.groupLabel !== 'Requests / Service Requests'),
        requestAssignedTasks: assignedTasks.filter((target) => target.groupLabel === 'Service Request Tasks' || target.groupLabel === 'Requests / Service Requests'),
        nonProjectCategories,
        timerTargetCounts: {
          assignments: Number(payload?.assignmentCount ?? assignedTasks.length),
          regularTasks: Number(payload?.regularTaskCount ?? assignedTasks.filter((target) => target.groupLabel !== 'Service Request Tasks' && target.groupLabel !== 'Requests / Service Requests').length),
          serviceRequestTasks: Number(payload?.serviceRequestTaskCount ?? assignedTasks.filter((target) => target.groupLabel === 'Service Request Tasks' || target.groupLabel === 'Requests / Service Requests').length),
          nonProject: Number(payload?.nonProjectCount ?? nonProjectCategories.length)
        },
        timerTargetAuthoritativeSources: Array.isArray(payload?.authoritativeSources) ? payload.authoritativeSources : [],
        timerTargetLoadError: error,
        [AUGMENTED_MARKER]: true
      };

      window.__projectPulseModule001Snapshot = enrichedSnapshot;
      window.dispatchEvent(new CustomEvent('projectpulse:module001-state', { detail: enrichedSnapshot }));
      window.dispatchEvent(new CustomEvent('projectpulse:module001-timer-targets', {
        detail: { payload, error, weekStart: snapshot.selectedWeekStart }
      }));
    };

    const enrichSnapshot = async (snapshot, force = false) => {
      if (!snapshot?.selectedWeekStart) return;
      const weekStart = String(snapshot.selectedWeekStart);
      const baseSnapshot = force ? { ...snapshot, [AUGMENTED_MARKER]: false } : snapshot;
      if (baseSnapshot?.[AUGMENTED_MARKER]) return;
      latestSnapshotRef.current.set(weekStart, baseSnapshot);

      if (!force && cacheRef.current.has(weekStart)) {
        publish(baseSnapshot, cacheRef.current.get(weekStart));
        return;
      }
      if (pendingRef.current.has(weekStart)) return;
      pendingRef.current.add(weekStart);

      try {
        const path = `/api/timesheet/timers/targets?weekStart=${encodeURIComponent(weekStart)}`;
        const payload = await authoritativeApi(path, { requiredCollections: ['targets'] });
        cacheRef.current.set(weekStart, payload);
        publish(latestSnapshotRef.current.get(weekStart) || baseSnapshot, payload);
      } catch (error) {
        publish(
          latestSnapshotRef.current.get(weekStart) || baseSnapshot,
          { targets: [] },
          error?.message || 'Unable to load assigned timer targets. Existing Timesheet activities remain available.'
        );
      } finally {
        pendingRef.current.delete(weekStart);
      }
    };

    const handleSnapshot = (event) => {
      void enrichSnapshot(event?.detail || window.__projectPulseModule001Snapshot || null);
    };
    const handleAuthSession = () => {
      cacheRef.current.clear();
      void enrichSnapshot(window.__projectPulseModule001Snapshot || null, true);
    };

    window.addEventListener('projectpulse:module001-state', handleSnapshot);
    window.addEventListener('projectpulse:auth-session-ready', handleAuthSession);
    queueMicrotask(() => void enrichSnapshot(window.__projectPulseModule001Snapshot || null));

    return () => {
      disposed = true;
      window.removeEventListener('projectpulse:module001-state', handleSnapshot);
      window.removeEventListener('projectpulse:auth-session-ready', handleAuthSession);
    };
  }, []);

  return <TimesheetEnhancementPortalV2 />;
}
