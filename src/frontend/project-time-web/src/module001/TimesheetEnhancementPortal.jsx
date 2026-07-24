import { useEffect, useRef } from 'react';
import TimesheetEnhancementPortalV2 from './TimesheetEnhancementPortalV2.jsx';

const UUID_PATTERN = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i;
const AUGMENTED_MARKER = '__module001AvailableTasksTimerTargets';

function canonicalWorkTypeGroup(task) {
  const workType = String(
    task?.workType
      || task?.canonicalWorkType
      || task?.projectWorkType
      || task?.assignmentWorkType
      || ''
  ).trim().toLowerCase();

  return workType === 'project' || workType === 'iqs'
    ? 'Regular Tasks'
    : 'Service Request Tasks';
}

function taskKey(task) {
  const assignmentId = String(task?.assignmentId || task?.projectAssignmentId || '');
  if (UUID_PATTERN.test(assignmentId)) return `assignment:${assignmentId}`;

  const projectId = String(task?.projectId || '');
  const taskId = String(task?.taskId || '');
  return projectId && taskId ? `project-task:${projectId}:${taskId}` : '';
}

function normalizeAvailableTask(task) {
  const assignmentId = String(task?.assignmentId || task?.projectAssignmentId || '');
  if (!UUID_PATTERN.test(assignmentId)) return null;

  const workType = String(
    task?.workType
      || task?.canonicalWorkType
      || task?.projectWorkType
      || task?.assignmentWorkType
      || ''
  ).trim();
  const serviceRequestNumber = String(
    task?.serviceRequestNumber
      || task?.requestNumber
      || task?.ticketNumber
      || ''
  ).trim();
  const groupLabel = canonicalWorkTypeGroup(task);
  const customerName = task?.customerName || task?.clientName || '';
  const selectionLabel = [
    serviceRequestNumber,
    customerName,
    task?.projectCode || task?.projectName,
    task?.taskName || task?.workItemName
  ].filter(Boolean).join(' · ') || 'Assigned task';

  return {
    ...task,
    assignmentId,
    projectAssignmentId: assignmentId,
    customerName,
    clientName: customerName,
    workType,
    canonicalWorkType: workType,
    serviceRequestNumber,
    serviceRequestId: serviceRequestNumber || task?.serviceRequestId || null,
    requestType: groupLabel === 'Service Request Tasks' ? 'service request' : '',
    selectionLabel,
    groupLabel
  };
}

function mergeAssignedTasks(existingTasks, availableTasks) {
  const merged = new Map();

  for (const task of existingTasks || []) {
    const key = taskKey(task);
    if (key) merged.set(key, task);
  }

  for (const task of availableTasks || []) {
    const key = taskKey(task);
    if (!key) continue;
    merged.set(key, {
      ...(merged.get(key) || {}),
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

    const publish = (snapshot, availableTasks) => {
      if (disposed || !snapshot?.selectedWeekStart) return;

      const assignedTasks = mergeAssignedTasks(snapshot.assignedTasks, availableTasks);
      const enrichedSnapshot = {
        ...snapshot,
        assignedTasks,
        regularAssignedTasks: assignedTasks.filter((task) => canonicalWorkTypeGroup(task) === 'Regular Tasks'),
        requestAssignedTasks: assignedTasks.filter((task) => canonicalWorkTypeGroup(task) === 'Service Request Tasks'),
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
          `/api/assignments/available-tasks?weekStart=${encodeURIComponent(weekStart)}`,
          {
            credentials: 'include',
            cache: 'no-store'
          }
        );

        if (!response.ok) return;

        const result = await response.json();
        const availableTasks = (Array.isArray(result?.tasks) ? result.tasks : [])
          .map(normalizeAvailableTask)
          .filter(Boolean);

        cacheRef.current.set(weekStart, availableTasks);
        publish(latestSnapshotRef.current.get(weekStart) || snapshot, availableTasks);
      } catch {
        // The canonical Timesheet snapshot remains usable if the shared available-task refresh fails.
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
