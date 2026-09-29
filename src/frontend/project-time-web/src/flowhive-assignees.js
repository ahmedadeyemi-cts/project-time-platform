export function assigneeKey(row) { return row?.projectContactId ? `contact:${row.projectContactId}` : row?.resourceUserId ? `user:${row.resourceUserId}` : `placeholder:${row?.resourceDisplayName || ''}`; }
export function taskAssignees(plan,wbs) { return (plan?.assignments || []).filter(row=>row.taskWbs===wbs); }
export function changeTaskAssignee(plan,wbs,person,selected) {
  if(!plan || !person || !plan.tasks?.some(task=>task.wbsNumber===wbs && !task.isSummary)) return plan;
  const current=taskAssignees(plan,wbs),key=assigneeKey(person),exists=current.some(row=>assigneeKey(row)===key);
  if(selected===exists) return plan;
  const next=selected ? [...(plan.assignments || []),{taskWbs:wbs,resourceUserId:person.resourceUserId || null,projectContactId:person.projectContactId || null,
    resourceDisplayName:person.displayName || person.resourceDisplayName || '',allocationPercent:100,plannedHours:0}]
    : (plan.assignments || []).filter(row=>row.taskWbs!==wbs || assigneeKey(row)!==key);
  // Adding a person does not duplicate the entire task estimate or redistribute existing allocations.
  return {...plan,assignments:next};
}
export function updateAssigneeHours(plan,wbs,key,field,value) {
  if(!['plannedHours','allocationPercent'].includes(field)) return plan;
  return {...plan,assignments:(plan.assignments||[]).map(row=>row.taskWbs===wbs && assigneeKey(row)===key ? {...row,[field]:value}:row)};
}
export function assigneeNames(rows) {return [...new Set((rows||[]).map(row=>row.resourceDisplayName).filter(Boolean))].join(', ') || 'Unassigned';}
