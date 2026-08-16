export const MODULE_DIRECTORY_AUTHORITY_CONTRACT = 'AUTHORITATIVE_RBAC_MODULE_DIRECTORY_V1';

function normalizedModuleNumber(value) {
  return String(value || '').trim().toUpperCase();
}

export function authorizedModulesFromNavigationState(projectModules, navigationState) {
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
