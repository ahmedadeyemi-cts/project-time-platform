import { useEffect, useRef } from 'react';
import TimesheetEnhancementPortalV2 from './TimesheetEnhancementPortalV2.jsx';

const UUID_PATTERN = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i;
const AUGMENTED_MARKER = '__module001AuthoritativeTimerTargets';

function normalizeAssignmentTarget(target) {
  const assignmentId = String(target?.assignmentId || target?.targetId || '');
  if (target?.targetType !== 'assignment' || !UUID_PATTERN.test(assignmentId)) return null;

  const workTaskCategory = String(target?.workTaskCategory || target?.workType || 'project_task')
    .trim()
    .toLowerCase();
  const serviceRequestNumber = String(target?.serviceRequestNumber || '').trim();

  return {
    assignmentId,
    projectAssignmentId: assignmentId,
    customerName: target?.customerName || '',
    clientName: target?.customerName || '',
    projectId: target?.projectId || null,
    projectCode: target?.projectCode || '',
    projectName: target?.projectName || '',
    taskId: target?.taskId || null,
    taskCode: target?.taskCode || '',
    taskName: target?.taskName || target?.selectionLabel || 'Assigned task',
    workTaskCategory,
    taskType: workTaskCategory,
    workType: [workTaskCategory, serviceRequestNumber].filter(Boolean).join(' '),
    serviceRequestNumber,
    serviceRequestId: serviceRequestNumber || null,
    requestType: workTaskCategory === 'service_request_task' ? 'service request' : '',
    selectionLabel: target?.selectionLabel || '',
    groupLabel: target?.groupLabel || (workTaskCategory === 'service_request_task'
      ? 'Service Request Tasks'
      : 'Regular Tasks')
  };
}

function mergeAssignedTasks(existingTasks, authoritativeTasks) {
  const merged = new Map();

  for (const task of existingTasks || []) {
    const assignmentId = String(task?.assignmentId || task?.projectAssignmentId || '');
    if (assignmentId) merged.set(assignmentId, task);
  }

  for (const task of authoritativeTasks || []) {
    const assignmentId = String(task?.assignmentId || task?.projectAssignmentId || '');
    if (!assignmentId) continue;
    merged.set(assignmentId, {
      ...(merged.get(assignmentId) || {}),
      ...task
    });
  }

  return [...merged.values()];
}

export default function TimesheetEnhancementPortal() {
  const cacheRef = useRef(new Map());
  const pendingRef = useRef(new Set());
  const latestSnapshotRef = useRef(new Map());

  useEffect(() => {
    let disposed = false;

    const publish = (snapshot, authoritativeTasks) => {
      if (disposed || !snapshot?.selectedWeekStart) return;

      const enrichedSnapshot = {
        ...snapshot,
        assignedTasks: mergeAssignedTasks(snapshot.assignedTasks, authoritativeTasks),
        [AUGMENTED_MARKER]: true
      };

      window.__projectPulseModule001Snapshot = enrichedSnapshot;
      window.dispatchEvent(new CustomEvent('projectpulse:module001-state', {
        detail: enrichedSnapshot
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
        const response = await fetch(
          `/api/timesheet/timers/targets?weekStart=${encodeURIComponent(weekStart)}`,
          {
            credentials: 'include',
            cache: 'no-store'
          }
        );

        if (!response.ok) return;

        const result = await response.json();
        const authoritativeTasks = (Array.isArray(result?.targets) ? result.targets : [])
          .map(normalizeAssignmentTarget)
          .filter(Boolean);

        cacheRef.current.set(weekStart, authoritativeTasks);
        publish(latestSnapshotRef.current.get(weekStart) || snapshot, authoritativeTasks);
      } catch {
        // The canonical Timesheet snapshot remains usable if the authoritative target refresh fails.
      } finally {
        pendingRef.current.delete(weekStart);
      }
    };

    const handleSnapshot = (event) => {
      void enrichSnapshot(event?.detail || window.__projectPulseModule001Snapshot || null);
    };

    window.addEventListener('projectpulse:module001-state', handleSnapshot);
    queueMicrotask(() => {
      void enrichSnapshot(window.__projectPulseModule001Snapshot || null);
    });

    return () => {
      disposed = true;
      window.removeEventListener('projectpulse:module001-state', handleSnapshot);
    };
  }, []);

  return <TimesheetEnhancementPortalV2 />;
}
