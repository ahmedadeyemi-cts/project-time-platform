import { normalizeGrant, pick, stable } from './role-permission-model.js';

// Verify the immutable publication receipt against both independent read surfaces.
// A successful write is not retried if readback fails or a newer policy supersedes it.
export function verifyPolicyPublication(request, receipt, detail, matrix) {
  const change = request?.changes?.[0];
  const version = Number(pick(receipt, 'versionNumber', 'VersionNumber', 0));
  const id = pick(receipt, 'policyVersionId', 'PolicyVersionId', '');
  if (request?.changes?.length !== 1 || !change || pick(receipt, 'status', 'Status', '') !== 'policy_published'
      || !Number.isInteger(version) || version <= Number(request.baseVersionNumber) || !id) {
    throw new Error('The server did not return a verifiable policy publication receipt. Refresh the policy before publishing again.');
  }
  const fail = (source, reason) => { throw new Error(`Policy version ${version} was published, but ${source} verification failed: ${reason}. Refresh and inspect the policy; do not publish the same change again.`); };
  const role = pick(detail, 'role', 'Role', {});
  if (pick(role, 'roleCode', 'RoleCode', '') !== change.roleCode || pick(detail, 'moduleCode', 'ModuleCode', '') !== change.moduleCode)
    fail('role detail', 'selected role/module does not match');
  for (const [source, payload] of [['role detail',detail],['permission matrix',matrix]]) {
    const current = pick(payload, 'policyVersion', 'PolicyVersion', {});
    if (Number(pick(current,'versionNumber','VersionNumber',0)) !== version || pick(current,'policyVersionId','PolicyVersionId','') !== id)
      fail(source, 'the current policy differs from the publication receipt');
    const grants = pick(payload, 'grants', 'Grants', null);
    if (!Array.isArray(grants)) fail(source, 'permission rows are missing');
    const matches = grants.filter(g => pick(g,'roleCode','RoleCode','') === change.roleCode && pick(g,'moduleCode','ModuleCode','') === change.moduleCode);
    if (source === 'role detail' && matches.length !== grants.length) fail(source,'permission rows belong to another role/module');
    if (stable(matches.map(normalizeGrant)) !== stable(change.grants.filter(g => g.isActive !== false).map(normalizeGrant)))
      fail(source, 'saved permissions do not match the submitted permissions');
  }
  return {versionNumber:version,policyVersionId:id,roleCode:change.roleCode,moduleCode:change.moduleCode};
}
