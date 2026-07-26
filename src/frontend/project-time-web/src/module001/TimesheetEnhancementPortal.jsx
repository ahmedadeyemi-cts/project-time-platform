import { useEffect, useRef } from 'react';
import TimesheetEnhancementPortalV2 from './TimesheetEnhancementPortalV2.jsx';

const AUGMENTED_MARKER = '__module001AuthoritativeTimerTargets';

function sessionHeaders() {
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    const token = session?.sessionToken || session?.token || session?.accessToken || '';
    const headers = token ? {
      Authorization: `Bearer ${token}`,
      'X-ProjectPulse-Session': token,
      'X-Project-Pulse-Session': token,
      'X-Session-Token': token
    } : {};
    const viewAs = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    if (viewAs?.userId) headers['X-ProjectPulse-View-As-User'] = viewAs.userId;
    return headers;
  } catch {
    return {};
  }
}

function targetKey(target) {
  return String(target?.selectionValue || `${target?.targetType || ''}:${target?.targetId || ''}`);
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

export default function TimesheetEnhancementPortal() {
  const cacheRef = useRef(new Map());
  const pendingRef = useRef(new Set());
  const latestSnapshotRef = useRef(new Map());

  useEffect(() => {
    let disposed = false;

    const publish = (snapshot, payload, error = '') => {
      if (disposed || !snapshot?.selectedWeekStart) return;
      const targets = Array.isArray(payload?.targets) ? payload.targets : [];
      const assignedTasks = targets.filter((target) => target.targetType === 'assignment');
      const nonProjectTargets = targets.filter((target) => target.targetType === 'category');
      const nonProjectCategories = mergeByKey(
        snapshot.nonProjectCategories,
        nonProjectTargets.map((target) => ({
          ...target,
          id: target.nonProjectTimeCategoryId || target.targetId,
          nonProjectTimeCategoryId: target.nonProjectTimeCategoryId || target.targetId,
          code: target.categoryCode,
          categoryCode: target.categoryCode,
          name: target.categoryName,
          categoryName: target.categoryName
        }))
      );
      const enrichedSnapshot = {
        ...snapshot,
        assignedTasks,
        regularAssignedTasks: assignedTasks.filter((target) => target.groupLabel === 'Regular Tasks'),
        requestAssignedTasks: assignedTasks.filter((target) => target.groupLabel === 'Service Request Tasks'),
        nonProjectCategories,
        timerTargetCounts: {
          assignments: Number(payload?.assignmentCount || assignedTasks.length),
          regularTasks: Number(payload?.regularTaskCount || 0),
          serviceRequestTasks: Number(payload?.serviceRequestTaskCount || 0),
          nonProject: Number(payload?.nonProjectCount || nonProjectTargets.length)
        },
        timerTargetLoadError: error,
        [AUGMENTED_MARKER]: true
      };

      window.__projectPulseModule001Snapshot = enrichedSnapshot;
      window.dispatchEvent(new CustomEvent('projectpulse:module001-state', { detail: enrichedSnapshot }));
      window.dispatchEvent(new CustomEvent('projectpulse:module001-timer-targets', {
        detail: { payload, error, weekStart: snapshot.selectedWeekStart }
      }));
    };

    const enrichSnapshot = async (snapshot) => {
      if (!snapshot?.selectedWeekStart || snapshot?.[AUGMENTED_MARKER]) return;
      const weekStart = String(snapshot.selectedWeekStart);
      latestSnapshotRef.current.set(weekStart, snapshot);

      if (cacheRef.current.has(weekStart)) {
        publish(snapshot, cacheRef.current.get(weekStart));
        return;
      }
      if (pendingRef.current.has(weekStart)) return;
      pendingRef.current.add(weekStart);

      try {
        const path = `/api/timesheet/timers/targets?weekStart=${encodeURIComponent(weekStart)}`;
        const response = await fetch(path, {
          method: 'GET',
          credentials: 'include',
          cache: 'no-store',
          headers: {
            ...sessionHeaders(),
            'Cache-Control': 'no-cache',
            Pragma: 'no-cache'
          }
        });
        const raw = await response.text();
        let payload;
        try {
          payload = raw ? JSON.parse(raw) : {};
        } catch {
          throw new Error(`${path} returned non-JSON content.`);
        }
        if (!response.ok) {
          throw new Error(payload?.message || payload?.detail || `${path} returned HTTP ${response.status}`);
        }
        if (!Array.isArray(payload?.targets)) {
          throw new Error(`${path} returned an incomplete timer-target payload.`);
        }

        cacheRef.current.set(weekStart, payload);
        publish(latestSnapshotRef.current.get(weekStart) || snapshot, payload);
      } catch (error) {
        publish(latestSnapshotRef.current.get(weekStart) || snapshot, { targets: [] }, error?.message || 'Unable to load assigned timer targets.');
      } finally {
        pendingRef.current.delete(weekStart);
      }
    };

    const handleSnapshot = (event) => {
      void enrichSnapshot(event?.detail || window.__projectPulseModule001Snapshot || null);
    };

    window.addEventListener('projectpulse:module001-state', handleSnapshot);
    queueMicrotask(() => void enrichSnapshot(window.__projectPulseModule001Snapshot || null));

    return () => {
      disposed = true;
      window.removeEventListener('projectpulse:module001-state', handleSnapshot);
    };
  }, []);

  return <TimesheetEnhancementPortalV2 />;
}
