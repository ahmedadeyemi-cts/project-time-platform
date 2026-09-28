export function workRegisterPeople(items = []) {
  const names = new Map();
  for (const item of items) {
    for (const raw of [item.projectManager, item.projectCoordinator, item.accountExecutive,
      item.solutionArchitect, item.insideSales, ...(item.assignedEngineers || [])]) {
      const name = String(raw || '').replace(/\s+/g, ' ').trim();
      if (name && !names.has(name.toLocaleLowerCase())) names.set(name.toLocaleLowerCase(), name);
    }
  }
  return [...names.values()].sort((a, b) => a.localeCompare(b));
}
