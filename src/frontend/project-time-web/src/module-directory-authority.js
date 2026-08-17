export const MODULE_DIRECTORY_AUTHORITY_CONTRACT = 'AUTHORITATIVE_RBAC_MODULE_DIRECTORY_V1';
export const SHARED_WORKSPACE_MODULE_AUTHORITY_CONTRACT = 'SHARED_WORKSPACE_MODULE_AUTHORITY_V1';

function normalizedModuleNumber(value) {
  return String(value || '').trim().toUpperCase();
}

function currentViewAsUserId() {
  if (typeof window === 'undefined') return '';
  try {
    const value = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    return String(value?.userId || '').trim();
  } catch {
    return '';
  }
}

function publishedWorkspaceAuthority() {
  if (typeof window === 'undefined') return null;
  const published = window.__projectPulseAuthorizedWorkspaceNavigation;
  if (!published || published.contract !== SHARED_WORKSPACE_MODULE_AUTHORITY_CONTRACT) return null;
  if (String(published.viewAsUserId || '') !== currentViewAsUserId()) return null;
  return published;
}

export function authorizedModulesFromEffectiveNavigationState(projectModules, navigationState) {
  if (!navigationState || navigationState.state !== 'ready') return null;

  const denied = new Set(
    (navigationState.deniedModuleNumbers || []).map(normalizedModuleNumber).filter(Boolean)
  );
  const retired = new Set(
    (navigationState.retiredModuleNumbers || []).map(normalizedModuleNumber).filter(Boolean)
  );

  return (projectModules || []).filter((module) => {
    const moduleNumber = normalizedModuleNumber(module?.moduleNumber);
    return Boolean(moduleNumber) && !denied.has(moduleNumber) && !retired.has(moduleNumber);
  });
}

export function authorizedModulesFromNavigationState(projectModules, navigationState) {
  const published = publishedWorkspaceAuthority();
  if (published) {
    if (published.state !== 'ready') return [];
    const allowed = new Set(
      (published.moduleNumbers || []).map(normalizedModuleNumber).filter(Boolean)
    );
    return (projectModules || []).filter((module) => allowed.has(normalizedModuleNumber(module?.moduleNumber)));
  }

  return authorizedModulesFromEffectiveNavigationState(projectModules, navigationState);
}
