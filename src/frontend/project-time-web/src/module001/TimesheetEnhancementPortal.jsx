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

function projectTaskKey(task) {
  const projectId = String(task?.projectId || '');
  const taskId = String(task?.taskId || '');
  return projectId && taskId ? `${projectId}:${taskId}` : '';
}

function taskKey(task) {
  const assignmentId = String(task?.assignmentId || task?.projectAssignmentId || '');
  if (UUID_PATTERN.test(assignmentId)) return `assignment:${assignmentId}`;

  const projectTask = projectTaskKey(task);
  return projectTask ? `project-task:${projectTask}` : '';
}

function buildWorkQueueAssignmentIndex(tasks) {
  const assignmentIndex = new Map();

  for (const task of tasks || []) {
    const assignmentId = String(task?.assignmentId || task?.projectAssignmentId || '');
    const key = projectTaskKey(task);
    if (!key || !UUID_PATTERN.test(assignmentId)) continue;
    assignmentIndex.set(key, task);
  }

  return assignmentIndex;
}

function normalizeAvailableTask(task, assignmentIndex) {
  const matchingAssignment = assignmentIndex.get(projectTaskKey(task)) || {};
  const assignmentId = String(
    task?.assignmentId
      || task?.projectAssignmentId
      || matchingAssignment?.assignmentId
      || matchingAssignment?.projectAssignmentId
      || ''
  );
  if (!UUID_PATTERN.test(assignmentId)) return null;

  const source = {
    ...matchingAssignment,
    ...task
  };
  const workType = String(
    source?.workType
      || source?.canonicalWorkType
      || source?.projectWorkType
      || source?.assignmentWorkType
      || ''
  ).trim();
  const serviceRequestNumber = String(
    source?.serviceRequestNumber
      || source?.requestNumber
      || source?.ticketNumber
      || ''
  ).trim();
  const groupLabel = canonicalWorkTypeGroup(source);
  const customerName = source?.customerName || source?.clientName || '';
  const selectionLabel = [
    serviceRequestNumber,
    customerName,
    source?.projectCode || source?.projectName,
    source?.taskName || source?.workItemName
  ].filter(Boolean).join(' · ') || 'Assigned task';

  return {
    ...source,
    assignmentId,
    projectAssignmentId: assignmentId,
    customerName,
    clientName: customerName,
    workType,
    canonicalWorkType: workType,
    serviceRequestNumber,
    serviceRequestId: serviceRequestNumber || source?.serviceRequestId || null,
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
        const [availableResult, workQueueResult] = await Promise.allSettled([
          fetch(
            `/api/assignments/available-tasks?weekStart=${encodeURIComponent(weekStart)}`,
            {
              credentials: 'include',
              cache: 'no-store'
            }
          ),
          fetch(
            `/api/timesheet/work-queue?weekStart=${encodeURIComponent(weekStart)}`,
            {
              credentials: 'include',
              cache: 'no-store'
            }
          )
        ]);

        if (availableResult.status !== 'fulfilled' || !availableResult.value.ok) return;

        const availablePayload = await availableResult.value.json();
        const workQueuePayload = workQueueResult.status === 'fulfilled' && workQueueResult.value.ok
          ? await workQueueResult.value.json()
          : { tasks: [] };
        const assignmentIndex = buildWorkQueueAssignmentIndex(
          Array.isArray(workQueuePayload?.tasks) ? workQueuePayload.tasks : []
        );
        const availableTasks = (Array.isArray(availablePayload?.tasks) ? availablePayload.tasks : [])
          .map((task) => normalizeAvailableTask(task, assignmentIndex))
          .filter(Boolean);

        cacheRef.current.set(weekStart, availableTasks);
        publish(latestSnapshotRef.current.get(weekStart) || snapshot, availableTasks);
      } catch {
        // The canonical Timesheet snapshot remains usable if the assignment join refresh fails.
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
