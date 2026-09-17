-- Generated from the application registry by reconcile-module-catalog.mjs.
-- Reconcile on every release; never rewrite an existing role/module decision.
BEGIN;
SELECT pg_advisory_xact_lock(hashtextextended('projectpulse-scoped-rbac-policy', 40));
LOCK TABLE scoped_role_policy_modules IN SHARE ROW EXCLUSIVE MODE;
CREATE TEMP TABLE desired_modules ON COMMIT DROP AS
SELECT * FROM jsonb_to_recordset(/*CATALOG_JSON*/::jsonb)
AS m(module_code text, module_name text, route_scope text, legacy_route text);
CREATE TEMP TABLE catalog_before ON COMMIT DROP AS SELECT * FROM scoped_role_policy_modules;
CREATE TEMP TABLE catalog_new ON COMMIT DROP AS
SELECT d.* FROM desired_modules d LEFT JOIN catalog_before b USING(module_code) WHERE b.module_code IS NULL;

DO $check$
BEGIN
  IF EXISTS (SELECT 1 FROM desired_modules d JOIN catalog_before b USING(module_code)
      WHERE b.route_scope NOT IN (d.route_scope, d.legacy_route)) THEN
    RAISE EXCEPTION 'MODULE_CATALOG_ROUTE_CONFLICT: existing module ID has an unrecognized route';
  END IF;
  IF EXISTS (SELECT 1 FROM desired_modules d JOIN catalog_before b
      ON lower(b.route_scope)=lower(d.route_scope) AND b.module_code<>d.module_code) THEN
    RAISE EXCEPTION 'MODULE_CATALOG_ROUTE_CONFLICT: canonical route is owned by another module ID';
  END IF;
END $check$;

INSERT INTO scoped_role_policy_modules(module_code,module_name,route_scope,current_state,permission_notes,source_url,is_active)
SELECT module_code,module_name,route_scope,'Pending authorization',
  'Automatically registered; restore and configure access in Role Administration.',
  'src/frontend/project-time-web/src/module-availability-registry.js',false FROM catalog_new;
UPDATE scoped_role_policy_modules m SET module_name=d.module_name, route_scope=d.route_scope
FROM desired_modules d WHERE m.module_code=d.module_code
AND (m.module_name,m.route_scope) IS DISTINCT FROM (d.module_name,d.route_scope);

-- A missing module is inactive AND explicitly denied in a new immutable policy.
-- Clone every existing grant, including denials and inactive grants, without edits.
DO $deny_new$
DECLARE old_id uuid; new_id uuid:=gen_random_uuid(); next_version integer;
BEGIN
  IF NOT EXISTS(SELECT 1 FROM catalog_new) THEN RETURN; END IF;
  SELECT policy_version_id INTO STRICT old_id FROM scoped_role_policy_versions WHERE policy_status='PUBLISHED';
  SELECT coalesce(max(version_number),0)+1 INTO next_version FROM scoped_role_policy_versions;
  INSERT INTO scoped_role_policy_versions(policy_version_id,version_number,policy_name,policy_status,source_name,source_sha256,policy_notes)
  VALUES(new_id,next_version,'Built-in catalog registration','DRAFT','Application module registry',/*CATALOG_SHA256*/,
    'Preserve existing permissions; deny newly registered modules until explicitly configured.');
  INSERT INTO scoped_role_policy_grants(policy_version_id,role_code,module_code,action_code,scope_code,grant_effect,
    conditions,delegated_authority,reason_required,audit_required,source_designation,source_notes,is_active)
  SELECT new_id,role_code,module_code,action_code,scope_code,grant_effect,conditions,delegated_authority,
    reason_required,audit_required,source_designation,source_notes,is_active
  FROM scoped_role_policy_grants WHERE policy_version_id=old_id;
  INSERT INTO scoped_role_policy_grants(policy_version_id,role_code,module_code,action_code,scope_code,grant_effect,
    conditions,delegated_authority,reason_required,audit_required,source_designation,source_notes,is_active)
  SELECT new_id,r.role_code,n.module_code,'MODULE_ACCESS','ORGANIZATION','DENY',
    '{"permissionLevel":"No Access","defaultPolicy":"NO_ACCESS_UNTIL_CONFIGURED","newModuleFailClosed":true}'::jsonb,
    false,false,true,'No Access','Automatic catalog registration',true
  FROM (SELECT DISTINCT CASE upper(role_code) WHEN 'ENGINEER' THEN 'ENGINEERING'
    WHEN 'ENGINEERING_TEAM_LEAD' THEN 'ENGINEERING_LEAD'
    WHEN 'PROJECT_MANAGER' THEN 'PROJECT_MANAGEMENT'
    WHEN 'PROJECT_MANAGEMENT_TEAM_LEAD' THEN 'PROJECT_MANAGEMENT_LEAD' WHEN 'PM_TEAM_LEAD' THEN 'PROJECT_MANAGEMENT_LEAD'
    WHEN 'ADMINISTRATOR' THEN 'SUPER_ADMINISTRATOR'
    ELSE upper(role_code) END AS role_code FROM app_roles WHERE is_active=true) r CROSS JOIN catalog_new n
  WHERE r.role_code<>'SUPER_ADMINISTRATOR';
  IF EXISTS (
    (SELECT to_jsonb(g)-'scoped_role_policy_grant_id'-'policy_version_id'-'created_at' FROM scoped_role_policy_grants g WHERE policy_version_id=old_id)
    EXCEPT ALL
    (SELECT to_jsonb(g)-'scoped_role_policy_grant_id'-'policy_version_id'-'created_at' FROM scoped_role_policy_grants g WHERE policy_version_id=new_id)
  ) THEN RAISE EXCEPTION 'MODULE_CATALOG_PERMISSION_PRESERVATION_FAILED'; END IF;
  UPDATE scoped_role_policy_versions SET policy_status='RETIRED',retired_at=now() WHERE policy_version_id=old_id;
  UPDATE scoped_role_policy_versions SET policy_status='PUBLISHED',published_at=now() WHERE policy_version_id=new_id;
  INSERT INTO scoped_role_policy_audit_events(policy_version_id,event_code,reason,previous_state,new_state,event_metadata)
  VALUES(new_id,'BUILTIN_MODULE_DEFAULT_DENY_PUBLISHED','New module registration only',
    jsonb_build_object('policyVersionId',old_id),jsonb_build_object('policyVersionId',new_id,'versionNumber',next_version),
    jsonb_build_object('releaseSha',/*RELEASE_SHA*/,'catalogSha256',/*CATALOG_SHA256*/));
END $deny_new$;

INSERT INTO scoped_role_policy_audit_events(event_code,reason,previous_state,new_state,event_metadata)
SELECT 'BUILTIN_MODULE_CATALOG_RECONCILED','Align module ID and name with the application registry',
  coalesce(to_jsonb(b),'{}'::jsonb),to_jsonb(m),
  jsonb_build_object('releaseSha',/*RELEASE_SHA*/,'catalogSha256',/*CATALOG_SHA256*/,'existingPermissionsPreserved',true)
FROM desired_modules d JOIN scoped_role_policy_modules m USING(module_code)
LEFT JOIN catalog_before b USING(module_code)
WHERE b.module_code IS NULL OR (b.module_name,b.route_scope) IS DISTINCT FROM (m.module_name,m.route_scope);
DO $verify$
BEGIN
  IF EXISTS(SELECT 1 FROM catalog_before b JOIN scoped_role_policy_modules m USING(module_code)
    WHERE (to_jsonb(b)-'module_name'-'route_scope') IS DISTINCT FROM (to_jsonb(m)-'module_name'-'route_scope')) THEN
    RAISE EXCEPTION 'MODULE_CATALOG_EXISTING_METADATA_CHANGED';
  END IF;
  IF EXISTS(SELECT 1 FROM desired_modules d LEFT JOIN scoped_role_policy_modules m USING(module_code)
    WHERE m.module_code IS NULL OR (m.module_name,m.route_scope) IS DISTINCT FROM (d.module_name,d.route_scope)) THEN
    RAISE EXCEPTION 'MODULE_CATALOG_RECONCILIATION_INCOMPLETE';
  END IF;
END $verify$;
INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('108_builtin_module_catalog_reconciliation','Release registry reconciliation preserving existing permissions',now())
ON CONFLICT(migration_id) DO NOTHING;
COMMIT;
SELECT 'MODULE_CATALOG_RECONCILIATION=PASS release=' || /*RELEASE_SHA*/ || ' catalog=' || /*CATALOG_SHA256*/;
