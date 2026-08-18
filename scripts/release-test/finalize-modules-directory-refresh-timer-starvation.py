from pathlib import Path


def replace_exact(source: str, old: str, new: str, label: str, expected: int = 1) -> str:
    count = source.count(old)
    if count != expected:
        raise SystemExit(f"{label}: expected {expected} occurrence(s), found {count}")
    return source.replace(old, new, expected)


def patch_modules_portal() -> None:
    path = Path('src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx')
    source = path.read_text()

    source = replace_exact(
        source,
        "const MODULE_DIRECTORY_STABLE_HYDRATION_CONTRACT = 'MODULE_DIRECTORY_STABLE_HYDRATION_V3';",
        "const MODULE_DIRECTORY_STABLE_HYDRATION_CONTRACT = 'MODULE_DIRECTORY_STABLE_HYDRATION_V3';\nconst MODULE_DIRECTORY_NONSTARVING_REFRESH_CONTRACT = 'MODULE_DIRECTORY_NONSTARVING_REFRESH_V4';",
        'Modules non-starving refresh contract'
    )

    source = replace_exact(
        source,
        "  const refreshTimer = useRef(null);\n  const directoryResolvedRef = useRef(false);",
        "  const refreshTimer = useRef(null);\n  const refreshPendingRef = useRef(false);\n  const directoryResolvedRef = useRef(false);",
        'Modules refresh pending ref'
    )

    old_refresh = """    const refresh = () => {
      ensurePersistentModulesLink(active);
      updateWorkspaceHeading(active);
      if (!active) return;

      expandAuthorizedNavigationGroups(expandedForDirectory.current);
      window.clearTimeout(refreshTimer.current);
      refreshTimer.current = window.setTimeout(() => {
        const nextModules = collectAuthorizedModules();
        const navigationResolved = authorizedModulesFromNavigationState(
          PROJECTPULSE_MODULES,
          window.__projectPulseEffectiveNavigation
        ) !== null;

        if (!navigationResolved && nextModules.length === 0) {
          requestAuthorityRefresh('module_directory_unresolved_authority');
          return;
        }

        directoryResolvedRef.current = true;
        authorityRefreshRequestedAtRef.current = 0;
        setDirectoryResolved(true);
        setModules((current) => moduleListsMatch(current, nextModules) ? current : nextModules);
      }, 80);
    };
"""
    new_refresh = """    let disposed = false;

    const resolveDirectory = () => {
      refreshTimer.current = null;
      if (disposed || !active) return;

      const nextModules = collectAuthorizedModules();
      const navigationResolved = authorizedModulesFromNavigationState(
        PROJECTPULSE_MODULES,
        window.__projectPulseEffectiveNavigation
      ) !== null;

      if (!navigationResolved && nextModules.length === 0) {
        requestAuthorityRefresh('module_directory_unresolved_authority');
      } else {
        directoryResolvedRef.current = true;
        authorityRefreshRequestedAtRef.current = 0;
        setDirectoryResolved(true);
        setModules((current) => moduleListsMatch(current, nextModules) ? current : nextModules);
      }

      if (refreshPendingRef.current) {
        refreshPendingRef.current = false;
        refreshTimer.current = window.setTimeout(resolveDirectory, 80);
      }
    };

    const refresh = ({ immediate = false } = {}) => {
      ensurePersistentModulesLink(active);
      updateWorkspaceHeading(active);
      if (!active) return;

      expandAuthorizedNavigationGroups(expandedForDirectory.current);
      if (refreshTimer.current !== null) {
        refreshPendingRef.current = true;
        return;
      }

      refreshTimer.current = window.setTimeout(resolveDirectory, immediate ? 0 : 80);
    };

    const refreshImmediately = () => refresh({ immediate: true });
"""
    source = replace_exact(source, old_refresh, new_refresh, 'Modules resettable refresh debounce')

    source = replace_exact(
        source,
        """    const resetForIdentity = () => {
      directoryResolvedRef.current = false;
      authorityRefreshRequestedAtRef.current = 0;
      setDirectoryResolved(false);
      setModules([]);
      requestAuthorityRefresh('module_directory_identity_changed');
      refresh();
    };

    refresh();
""",
        """    const resetForIdentity = () => {
      window.clearTimeout(refreshTimer.current);
      refreshTimer.current = null;
      refreshPendingRef.current = false;
      directoryResolvedRef.current = false;
      authorityRefreshRequestedAtRef.current = 0;
      setDirectoryResolved(false);
      setModules([]);
      requestAuthorityRefresh('module_directory_identity_changed');
      refresh({ immediate: true });
    };

    refresh({ immediate: true });
""",
        'Modules identity reset and initial refresh'
    )

    source = replace_exact(
        source,
        """    window.addEventListener('projectpulse:view-as-changed', resetForIdentity);
    window.addEventListener('projectpulse:auth-session-ready', refresh);
    window.addEventListener('projectpulse:module-availability-changed', refresh);
    window.addEventListener('projectpulse:permission-navigation-updated', refresh);
    window.addEventListener('projectpulse:workspace-authorization-updated', refresh);
    window.addEventListener('pageshow', refresh);

    return () => {
      observer?.disconnect();
      window.clearInterval(authorityPoll);
      window.removeEventListener('projectpulse:view-as-changed', resetForIdentity);
      window.removeEventListener('projectpulse:auth-session-ready', refresh);
      window.removeEventListener('projectpulse:module-availability-changed', refresh);
      window.removeEventListener('projectpulse:permission-navigation-updated', refresh);
      window.removeEventListener('projectpulse:workspace-authorization-updated', refresh);
      window.removeEventListener('pageshow', refresh);
      window.clearTimeout(refreshTimer.current);
      if (active) restoreNavigationGroups(expandedForDirectory.current);
    };
""",
        """    window.addEventListener('projectpulse:view-as-changed', resetForIdentity);
    window.addEventListener('projectpulse:auth-session-ready', refreshImmediately);
    window.addEventListener('projectpulse:module-availability-changed', refresh);
    window.addEventListener('projectpulse:permission-navigation-updated', refreshImmediately);
    window.addEventListener('projectpulse:workspace-authorization-updated', refreshImmediately);
    window.addEventListener('pageshow', refreshImmediately);

    return () => {
      disposed = true;
      observer?.disconnect();
      window.clearInterval(authorityPoll);
      window.removeEventListener('projectpulse:view-as-changed', resetForIdentity);
      window.removeEventListener('projectpulse:auth-session-ready', refreshImmediately);
      window.removeEventListener('projectpulse:module-availability-changed', refresh);
      window.removeEventListener('projectpulse:permission-navigation-updated', refreshImmediately);
      window.removeEventListener('projectpulse:workspace-authorization-updated', refreshImmediately);
      window.removeEventListener('pageshow', refreshImmediately);
      window.clearTimeout(refreshTimer.current);
      refreshTimer.current = null;
      refreshPendingRef.current = false;
      if (active) restoreNavigationGroups(expandedForDirectory.current);
    };
""",
        'Modules authority event and cleanup wiring'
    )

    source = replace_exact(
        source,
        'data-authority-contract={MODULE_DIRECTORY_AUTHORITY_CONTRACT} data-hydration-contract={MODULE_DIRECTORY_STABLE_HYDRATION_CONTRACT}>',
        'data-authority-contract={MODULE_DIRECTORY_AUTHORITY_CONTRACT} data-hydration-contract={MODULE_DIRECTORY_STABLE_HYDRATION_CONTRACT} data-refresh-contract={MODULE_DIRECTORY_NONSTARVING_REFRESH_CONTRACT}>',
        'Modules refresh DOM contract'
    )

    path.write_text(source)


def patch_focused_validator() -> None:
    path = Path('src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs')
    source = path.read_text()

    source = replace_exact(
        source,
        """  'MODULE_DIRECTORY_AUTHORITY_REFRESH_THROTTLE_MS',
  'authorityRefreshRequestedAtRef',
  '__projectPulsePermissionRefreshState',
  'directoryResolved={directoryResolved}'
""",
        """  'MODULE_DIRECTORY_AUTHORITY_REFRESH_THROTTLE_MS',
  'authorityRefreshRequestedAtRef',
  '__projectPulsePermissionRefreshState',
  'MODULE_DIRECTORY_NONSTARVING_REFRESH_V4',
  'refreshPendingRef',
  'const resolveDirectory = () =>',
  'if (refreshTimer.current !== null)',
  'refreshPendingRef.current = true',
  'immediate ? 0 : 80',
  'refresh({ immediate: true })',
  'data-refresh-contract={MODULE_DIRECTORY_NONSTARVING_REFRESH_CONTRACT}',
  'directoryResolved={directoryResolved}'
""",
        'Focused Modules non-starving refresh contracts'
    )

    source = replace_exact(
        source,
        "].forEach((contract) => requireText(modulesPortal, contract, 'nonblocking Modules directory hydration'));\n\nconst moduleManagement",
        "].forEach((contract) => requireText(modulesPortal, contract, 'nonblocking Modules directory hydration'));\nrejectText(\n  modulesPortal,\n  'window.clearTimeout(refreshTimer.current);\\n      refreshTimer.current = window.setTimeout(() =>',\n  'Modules directory refresh must not use a resettable debounce that can be starved by DOM mutations'\n);\n\nconst moduleManagement",
        'Focused Modules resettable debounce rejection'
    )

    source = replace_exact(
        source,
        "console.log('module_directory_authority=single_flight_preserved_ready_refresh');",
        "console.log('module_directory_authority=single_flight_preserved_ready_refresh');\nconsole.log('module_directory_refresh=nonstarving_coalesced_scheduler');",
        'Focused Modules non-starving result marker'
    )

    path.write_text(source)


def patch_directory_validator() -> None:
    path = Path('src/frontend/project-time-web/scripts/validate-modules-directory-page.mjs')
    source = path.read_text()

    source = replace_exact(
        source,
        """  'MODULE_DIRECTORY_AUTHORITY_REFRESH_THROTTLE_MS',
  'authorityRefreshRequestedAtRef',
  '__projectPulsePermissionRefreshState',
  'directoryResolvedRef',
""",
        """  'MODULE_DIRECTORY_AUTHORITY_REFRESH_THROTTLE_MS',
  'authorityRefreshRequestedAtRef',
  '__projectPulsePermissionRefreshState',
  'MODULE_DIRECTORY_NONSTARVING_REFRESH_V4',
  'refreshPendingRef',
  'const resolveDirectory = () =>',
  'if (refreshTimer.current !== null)',
  'refreshPendingRef.current = true',
  'immediate ? 0 : 80',
  'refresh({ immediate: true })',
  'data-refresh-contract={MODULE_DIRECTORY_NONSTARVING_REFRESH_CONTRACT}',
  'directoryResolvedRef',
""",
        'Modules directory non-starving refresh validator contracts'
    )

    source = replace_exact(
        source,
        """if (portal.includes('observer.observe(document.body')) {
  throw new Error('The Modules observer must not watch the whole body and retrigger itself from portal card rendering.');
}

if (portal.includes("module.moduleNumber ? `Module ${module.moduleNumber}` : module.group")) {
""",
        """if (portal.includes('observer.observe(document.body')) {
  throw new Error('The Modules observer must not watch the whole body and retrigger itself from portal card rendering.');
}

if (portal.includes('window.clearTimeout(refreshTimer.current);\\n      refreshTimer.current = window.setTimeout(() =>')) {
  throw new Error('The Modules directory must not reset its hydration timer for every root mutation.');
}

if (portal.includes("module.moduleNumber ? `Module ${module.moduleNumber}` : module.group")) {
""",
        'Modules directory resettable debounce rejection'
    )

    path.write_text(source)


def main() -> None:
    patch_modules_portal()
    patch_focused_validator()
    patch_directory_validator()
    Path('scripts/release-test/finalize-modules-directory-refresh-timer-starvation.py').unlink(missing_ok=True)


if __name__ == '__main__':
    main()
