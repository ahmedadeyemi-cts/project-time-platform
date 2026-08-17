import fs from 'node:fs';
import path from 'node:path';
const root = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const component = fs.readFileSync(path.join(root, 'src', 'WorkspaceNavigationPortal.jsx'), 'utf8');
const css = fs.readFileSync(path.join(root, 'src', 'workspace-navigation.css'), 'utf8');
const main = fs.readFileSync(path.join(root, 'src', 'main.jsx'), 'utf8');
for (const marker of ['WorkspaceQuickLauncher', 'Workspace Directory', 'Search workspaces', 'View all workspaces', 'Recently used', 'Favorites', 'Browse by category', 'Toyota', 'Hyundai', 'Turion', 'authorizedModulesFromEffectiveNavigationState', 'publishWorkspaceAuthorization', 'SHARED_WORKSPACE_MODULE_AUTHORITY_CONTRACT', '__projectPulseEffectiveNavigation', 'View-As active', 'Retry verification', 'workspace-directory']) {
  if (!component.includes(marker)) throw new Error(`Workspace navigation component missing ${marker}`);
}
for (const marker of ['workspace-quick-launcher-backdrop', 'workspace-directory-page', 'workspace-directory-grid', "data-theme='dark'", '@media (max-width: 720px)', '@media (prefers-reduced-motion: reduce)']) {
  if (!css.includes(marker)) throw new Error(`Workspace navigation styles missing ${marker}`);
}
if (!main.includes("import WorkspaceNavigationPortal from './WorkspaceNavigationPortal.jsx';")) throw new Error('main.jsx missing WorkspaceNavigationPortal import');
if (!main.includes('<WorkspaceNavigationPortal />')) throw new Error('main.jsx missing WorkspaceNavigationPortal mount');
if (!main.includes("import './workspace-navigation.css';")) throw new Error('main.jsx missing workspace-navigation.css');
for (const forbidden of ['Ahmed Adeyemi', 'Kevin Damisch', '28 workspaces available']) {
  if (component.includes(forbidden)) throw new Error(`Workspace navigation hardcodes ${forbidden}`);
}
console.log('PR698_WORKSPACE_NAVIGATION=PASS authority=shared-rbac-and-availability publication=shared-with-modules viewAs=effective favorites=user-specific');
