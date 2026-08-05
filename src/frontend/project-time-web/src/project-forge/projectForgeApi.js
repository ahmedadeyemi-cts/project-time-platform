import { clientMutationId, taskId, taskRevision, taskSource } from './projectForgeModel.js';

function storedSession() {
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    if (!session?.sessionToken) return null;
    if (session.expiresAt && Date.now() >= Date.parse(session.expiresAt)) return null;
    return session;
  } catch {
    return null;
  }
}

function headers(extra = {}) {
  const session = storedSession();
  return {
    ...(session?.sessionToken ? {
      Authorization: `Bearer ${session.sessionToken}`,
      'X-ProjectPulse-Session': session.sessionToken
    } : {}),
    'X-ProjectPulse-Module-Number': '033',
    ...extra
  };
}

export class ProjectForgeApiError extends Error {
  constructor(message, status, body) {
    super(message);
    this.name = 'ProjectForgeApiError';
    this.status = status;
    this.body = body;
  }
}

export async function projectForgeRequest(path, options = {}) {
  const response = await fetch(path, {
    cache: options.method ? undefined : 'no-store',
    credentials: 'same-origin',
    ...options,
    headers: headers(options.headers)
  });
  const contentType = response.headers.get('content-type') || '';
  const body = contentType.includes('application/json') ? await response.json() : null;
  if (!response.ok) {
    throw new ProjectForgeApiError(
      body?.message || body?.detail || `${path} returned HTTP ${response.status}`,
      response.status,
      body
    );
  }
  return body;
}

export function projectForgeSend(path, method, body, options = {}) {
  return projectForgeRequest(path, {
    ...options,
    method,
    headers: { 'Content-Type': 'application/json', ...(options.headers || {}) },
    body: JSON.stringify(body)
  });
}

function mutationEnvelope(task, extra = {}) {
  return {
    recordSource: taskSource(task),
    ...(taskSource(task) === 'review_plan' ? { planId: task.planId || task.projectForgePlanId } : {}),
    expectedRevision: taskRevision(task),
    clientMutationId: clientMutationId(),
    ...extra
  };
}

export const projectForgeApi = Object.freeze({
  bootstrap({ projectManagerUserId = '', projectId = '', workspace = 'canonical', planId = '', signal } = {}) {
    const query = new URLSearchParams();
    if (projectManagerUserId) query.set('projectManagerUserId', projectManagerUserId);
    if (projectId) query.set('projectId', projectId);
    query.set('workspace', workspace);
    if (workspace === 'review_plan' && planId) query.set('planId', planId);
    return projectForgeRequest(`/api/project-forge/bootstrap?${query}`, { signal });
  },

  createTask(projectId, task) {
    return projectForgeSend(`/api/project-forge/projects/${projectId}/tasks`, 'POST', {
      clientMutationId: clientMutationId(),
      ...task
    });
  },

  updateDetails(task, changes) {
    return projectForgeSend(`/api/project-forge/tasks/${taskId(task)}/details`, 'PATCH', mutationEnvelope(task, changes));
  },

  updateCompositeTask(task, changes) {
    return projectForgeSend(`/api/project-forge/tasks/${taskId(task)}/composite`, 'PATCH', mutationEnvelope(task, changes));
  },

  updateWorkflow(task, category, position = {}) {
    return projectForgeSend(`/api/project-forge/tasks/${taskId(task)}/workflow`, 'PATCH', mutationEnvelope(task, {
      kanbanCategory: category,
      ...(position.beforeTaskId ? { beforeTaskId: position.beforeTaskId } : {}),
      ...(position.afterTaskId ? { afterTaskId: position.afterTaskId } : {}),
      ...position.workflow
    }));
  },

  updateSchedule(task, startDate, dueDate, interaction = 'move', options = {}) {
    return projectForgeSend(`/api/project-forge/tasks/${taskId(task)}/schedule`, 'PATCH', mutationEnvelope(task, {
      interaction,
      startDate,
      dueDate,
      cascadeSuccessors: false,
      ...options
    }));
  },

  updateDecision(task, decision) {
    return projectForgeSend(`/api/project-forge/tasks/${taskId(task)}/decision`, 'PATCH', mutationEnvelope(task, decision));
  },

  assignTask(task, assignment) {
    return projectForgeSend(`/api/project-forge/tasks/${taskId(task)}/assignee`, 'PUT', mutationEnvelope(task, assignment));
  },

  archiveTask(task, reason) {
    return projectForgeSend(`/api/project-forge/tasks/${taskId(task)}`, 'DELETE', mutationEnvelope(task, { reason }));
  },

  createDependency(projectId, task, dependency) {
    return projectForgeSend(`/api/project-forge/projects/${projectId}/task-dependencies`, 'POST', mutationEnvelope(task, {
      expectedRevision: taskSource(task) === 'canonical' ? null : task.planningRevision ?? task.revision ?? taskRevision(task),
      ...dependency
    }));
  },

  updateDependency(task, dependency) {
    const dependencyId = dependency.taskDependencyId || dependency.dependencyId;
    return projectForgeSend(`/api/project-forge/task-dependencies/${dependencyId}`, 'PATCH', mutationEnvelope(task, dependency));
  },

  deleteDependency(task, dependency) {
    const dependencyId = dependency.taskDependencyId || dependency.dependencyId;
    return projectForgeSend(`/api/project-forge/task-dependencies/${dependencyId}`, 'DELETE', mutationEnvelope(task, {
      expectedRevision: dependency.revision ?? dependency.dependencyRevision ?? taskRevision(task),
      predecessorTaskId: dependency.predecessorTaskId,
      successorTaskId: dependency.successorTaskId,
      dependencyType: dependency.dependencyType || 'FS',
      lagWorkingDays: Number(dependency.lagWorkingDays || 0)
    }));
  },

  saveEstimate(task, estimate) {
    return projectForgeSend(`/api/project-forge/plan-tasks/${taskId(task)}/estimate`, 'PATCH', {
      ...estimate,
      expectedVersion: taskRevision(task),
      clientMutationId: clientMutationId()
    });
  },

  completeReview(planId, task, reviewNote, decision = 'completed') {
    return projectForgeSend(`/api/project-forge/plans/${planId}/tasks/${taskId(task)}/review-completion`, 'POST', {
      reviewNote,
      expectedRevision: taskRevision(task),
      decision,
      clientMutationId: clientMutationId()
    });
  }
});
