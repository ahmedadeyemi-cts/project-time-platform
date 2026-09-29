// Normalize only transport types. Never invent dates, identities, dependencies or effort.
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export class FlowHiveInputError extends Error {
  constructor(path, message) { super(`${path}: ${message}`); this.responseBody = { status: 'invalid_plan_field', issues: [{ code: 'invalid_plan_field', severity: 'error', path, message }] }; }
}
export function optionalGuid(value, path) {
  if (value == null || value === '') return null;
  if (typeof value !== 'string' || !guid.test(value)) throw new FlowHiveInputError(path, 'Select a valid saved identity or clear this optional field.');
  return value;
}
export function optionalDate(value, path) {
  if (value == null || value === '') return null;
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value) || !Number.isFinite(Date.parse(`${value}T00:00:00Z`)) || new Date(`${value}T00:00:00Z`).toISOString().slice(0,10) !== value)
    throw new FlowHiveInputError(path, 'Enter a valid date, or leave this optional date blank.');
  return value;
}
function numeric(value, path, { integer=false, fallback=0 }={}) {
  if (value == null) return fallback;
  if (value === '' || typeof value === 'boolean' || !['string','number'].includes(typeof value)) throw new FlowHiveInputError(path, 'Enter a number.');
  const parsed = Number(value);
  if (!Number.isFinite(parsed) || (integer && !Number.isInteger(parsed))) throw new FlowHiveInputError(path, integer ? 'Enter a whole number of working days.' : 'Enter a finite number.');
  return parsed;
}
const taskLists = ['detailedSteps','inputs','outputs','acceptanceCriteria','validationSteps','customerResponsibilities','usSignalResponsibilities','prerequisites','risks','openQuestions','products','platforms','manufacturers','models','softwareVersions','firmwareVersions','licensingRequirements','quantities','tools','systems','interfaces','integrationPoints','accessRequirements','rollbackSteps','assumptions','requiredRoles'];
function rows(value,path) { if (value == null) return []; if (!Array.isArray(value)) throw new FlowHiveInputError(path,'Expected a list.'); return value; }
function citationIds(value,path) { return rows(value,path).map((id,i)=>numeric(id,`${path}[${i}]`,{integer:true})); }
export function flowHivePlanRequest(plan, projectId) {
  if (!plan || typeof plan!=='object' || Array.isArray(plan) || 'nativeEvent' in plan) throw new FlowHiveInputError('plan','Load a project working copy before saving.');
  if (!projectId || plan.projectId !== projectId) throw new FlowHiveInputError('projectId','The plan does not belong to the selected project. Reload that project.');
  const result = { ...plan, projectId:optionalGuid(projectId,'projectId'), planId:optionalGuid(plan.planId,'planId'),
    projectStartDate:optionalDate(plan.projectStartDate,'projectStartDate'), projectEndDate:optionalDate(plan.projectEndDate,'projectEndDate') };
  result.tasks = rows(plan.tasks,'tasks').map((task,index)=>{
    const path=`WBS ${task?.wbsNumber || index+1}`;
    if (!task || typeof task !== 'object') throw new FlowHiveInputError(path,'Task information is missing.');
    const row={...task};
    for (const key of ['clientTaskId','canonicalTaskId']) row[key]=optionalGuid(task[key],`${path}.${key}`);
    for (const key of ['constraintDate','estimatedStartDate','estimatedFinishDate']) row[key]=optionalDate(task[key],`${path}.${key}`);
    row.durationWorkingDays=numeric(task.durationWorkingDays,`${path}.durationWorkingDays`,{integer:true});
    row.percentComplete=numeric(task.percentComplete,`${path}.percentComplete`);
    row.remainingEffortHours=numeric(task.remainingEffortHours,`${path}.remainingEffortHours`);
    for(const key of taskLists) if(task[key]!=null) row[key]=rows(task[key],`${path}.${key}`).map((value,i)=>{if(typeof value!=='string')throw new FlowHiveInputError(`${path}.${key}[${i}]`,'Enter text, not an object.');return value;});
    row.citationIds=citationIds(task.citationIds,`${path}.citationIds`); return row;
  });
  result.dependencies=rows(plan.dependencies,'dependencies').map((row,i)=>({...row,lagWorkingDays:numeric(row.lagWorkingDays,`dependencies[${i}].lagWorkingDays`,{integer:true})}));
  result.assignments=rows(plan.assignments,'assignments').map((row,i)=>({...row,
    resourceUserId:optionalGuid(row.resourceUserId,`assignments[${i}].resourceUserId`),projectContactId:optionalGuid(row.projectContactId,`assignments[${i}].projectContactId`),
    allocationPercent:numeric(row.allocationPercent,`assignments[${i}].allocationPercent`,{fallback:100}),plannedHours:numeric(row.plannedHours,`assignments[${i}].plannedHours`)}));
  result.milestones=rows(plan.milestones,'milestones').map((row,i)=>({...row,targetDate:optionalDate(row.targetDate,`milestones[${i}].targetDate`)}));
  if (plan.celarAiConfidence != null) result.celarAiConfidence=numeric(plan.celarAiConfidence,'celarAiConfidence');
  if (plan.celarAiCitationIds != null) result.celarAiCitationIds=citationIds(plan.celarAiCitationIds,'celarAiCitationIds');
  return result;
}
export function workingCopyRequest(plan, projectId, expectedRowVersion) {
  return {plan:flowHivePlanRequest(plan,projectId),expectedRowVersion:optionalGuid(expectedRowVersion,'expectedRowVersion')};
}
export function flowHiveErrorText(error) {
  const body=error?.responseBody || {};
  const issues=(body.issues || []).filter(item=>item && typeof item.message==='string');
  const text=issues.length ? issues.slice(0,4).map(item=>`${item.path || 'Entry'}: ${item.message}`).join(' ') : error?.message || 'The request could not be completed.';
  return `${text}${body.correlationId ? ` Reference: ${body.correlationId}` : ''}`;
}
