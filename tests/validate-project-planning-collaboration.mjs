// Compatibility entry point retained for older CI jobs. The current Migration
// 095 and project-scoped access contract are validated by the authoritative
// collaboration-access suite.
await import('./validate-project-planning-collaboration-access.mjs');
console.log('project_planning_collaboration_compatibility=PROJECT_PLANNING_COLLABORATION_V1');
