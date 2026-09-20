export function isFlowHiveArchived(project) {
  return project?.isArchived === true || ['closed', 'completed', 'cancelled', 'canceled', 'archived']
    .includes(String(project?.status || '').trim().toLowerCase());
}

export function filterFlowHiveProjects(projects, { archived = false, customer = 'all', status = 'all', search = '' } = {}) {
  const query = search.trim().toLowerCase();
  return projects.filter((project) => isFlowHiveArchived(project) === archived
    && (customer === 'all' || project.customerName === customer)
    && (status === 'all' || project.status === status)
    && (!query || [project.projectCode, project.projectName, project.customerName, project.projectManagerName,
      project.accountExecutiveName, project.status].some(value => String(value || '').toLowerCase().includes(query))));
}
