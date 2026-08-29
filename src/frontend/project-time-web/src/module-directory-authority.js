import { hasAnyEffectiveRole, readEffectiveRoleAuthority } from './effective-role-authority.js';

export const MODULE_DIRECTORY_AUTHORITY_CONTRACT = 'AUTHORITATIVE_RBAC_MODULE_DIRECTORY_V1';
export const SHARED_WORKSPACE_MODULE_AUTHORITY_CONTRACT = 'SHARED_WORKSPACE_MODULE_AUTHORITY_V1';

const MODULE001B_ROLES = new Set(['PROJECT_TEAM_COORDINATOR', 'SUPER_ADMINISTRATOR']);

function normalizedModuleNumber(value) {
  return String(value || '').trim().toUpperCase();
}

function module001BRoleAllowed() {
  const authority = readEffectiveRoleAuthority();
  return authority.ready && hasAnyEffectiveRole(authority, MODULE001B_ROLES);
}

function applyModule001BRoleBoundary(modules) {
  const allowed = module001BRoleAllowed();
  return (modules || []).filter((module) => (
    normalizedModuleNumber(module?.moduleNumber) !== '001B' || allowed
  ));
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

  return applyModule001BRoleBoundary((projectModules || []).filter((module) => {
    const moduleNumber = normalizedModuleNumber(module?.moduleNumber);
    return Boolean(moduleNumber) && !denied.has(moduleNumber) && !retired.has(moduleNumber);
  }));
}

export function authorizedModulesFromNavigationState(projectModules, navigationState) {
  const published = publishedWorkspaceAuthority();
  if (published) {
    // Shared workspace authorization is published asynchronously. While it is
    // still initializing, retain the already-authorized effective-navigation
    // result instead of converting the loading state into an empty directory.
    // A completed shared result remains authoritative once it is ready.
    if (published.state !== 'ready') {
      return authorizedModulesFromEffectiveNavigationState(projectModules, navigationState);
    }

    const allowed = new Set(
      (published.moduleNumbers || []).map(normalizedModuleNumber).filter(Boolean)
    );
    return applyModule001BRoleBoundary(
      (projectModules || []).filter((module) => allowed.has(normalizedModuleNumber(module?.moduleNumber)))
    );
  }

  return authorizedModulesFromEffectiveNavigationState(projectModules, navigationState);
}
