// Fetch every authorized page; never silently omit projects after the first 250.
export async function loadFinancialPortfolio(read, parameters, isCurrent = () => true) {
  const query = new URLSearchParams(parameters);
  const projects = new Map();
  let first;
  let offset = 0;
  do {
    if (!isCurrent()) return null;
    query.set('offset', String(offset));
    const page = await read(`/api/project-financials/portfolio?${query}`);
    if (!isCurrent()) return null;
    if (!Array.isArray(page?.projects)) throw new Error('Project portfolio returned an invalid response.');
    first ||= page;
    for (const project of page.projects) projects.set(project.projectId, project);
    if (page.nextOffset == null) break;
    if (!Number.isSafeInteger(page.nextOffset) || page.nextOffset <= offset || !page.projects.length) {
      throw new Error('Project portfolio pagination did not advance. Refresh and try again.');
    }
    offset = page.nextOffset;
  } while (true);
  return { ...first, projects: [...projects.values()], nextOffset: null };
}
