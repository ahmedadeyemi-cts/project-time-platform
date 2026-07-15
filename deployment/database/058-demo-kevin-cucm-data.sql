\set ON_ERROR_STOP on
\pset pager off
BEGIN;
SET LOCAL lock_timeout='20s';
SET LOCAL statement_timeout='8min';

DO $do$
DECLARE
  kevin uuid := 'faf19aa4-47b2-45ab-bc6d-0351ea5d72e0';
  client uuid := 'f2bded8b-dd27-43b5-bed1-de635f7153d2';
  q2 uuid := '04200000-0000-4000-8000-000000000201';
  july uuid := '04200000-0000-4000-8000-000000000202';
  card uuid := '04200000-0000-4000-8000-000000000203';
  line uuid := '04200000-0000-4000-8000-000000000204';
  actor uuid; pjson jsonb; tjson jsonb; ajson jsonb; sjson jsonb; ejson jsonb; cjson jsonb; ljson jsonb;
  project_template jsonb; task_template jsonb; assignment_template jsonb; sheet_template jsonb; entry_template jsonb; card_template jsonb; line_template jsonb;
  task_ids uuid[] := ARRAY[
   '04200000-0000-4000-8000-000000000211'::uuid,'04200000-0000-4000-8000-000000000212'::uuid,'04200000-0000-4000-8000-000000000213'::uuid,'04200000-0000-4000-8000-000000000214'::uuid,'04200000-0000-4000-8000-000000000215'::uuid,
   '04200000-0000-4000-8000-000000000221'::uuid,'04200000-0000-4000-8000-000000000222'::uuid,'04200000-0000-4000-8000-000000000223'::uuid,'04200000-0000-4000-8000-000000000224'::uuid,'04200000-0000-4000-8000-000000000225'::uuid];
  names text[] := ARRAY['Discovery and Readiness Assessment','Pre-Upgrade Validation and Backup Coordination','CUCM 15.0 Migration Planning and Implementation','Post-Upgrade Call-Flow and Device Validation','User Acceptance, Remediation, and Handoff','Discovery and Readiness Assessment','Pre-Upgrade Validation and Backup Coordination','CUCM 15.0 Migration Planning and Implementation','Post-Upgrade Call-Flow and Device Validation','User Acceptance, Remediation, and Handoff'];
  descriptions text[] := ARRAY[
   'DEMO TEST DATA — Reviewed CUCM 12.5 topology, publisher/subscriber roles, device counts, dial plan, SIP trunks, voicemail, CER, and readiness risks.',
   'DEMO TEST DATA — Confirmed DRS backups, upgrade path, VM sizing, certificates, DNS/NTP, media requirements, rollback checkpoints, and change prerequisites.',
   'DEMO TEST DATA — Prepared the CUCM 15.0 runbook, staged activities, coordinated windows, validated media, and supported publisher/subscriber sequencing.',
   'DEMO TEST DATA — Validated registration, internal/PSTN dialing, voicemail, hunt pilots, route and translation patterns, SIP trunks, CER, and firmware behavior.',
   'DEMO TEST DATA — Completed user acceptance, remediated defects, documented known issues, updated support notes, and prepared operational handoff.',
   'DEMO TEST DATA — Reviewed CUCM 12.5 topology, publisher/subscriber roles, device counts, dial plan, SIP trunks, voicemail, CER, and readiness risks.',
   'DEMO TEST DATA — Confirmed DRS backups, upgrade path, VM sizing, certificates, DNS/NTP, media requirements, rollback checkpoints, and change prerequisites.',
   'DEMO TEST DATA — Prepared the CUCM 15.0 runbook, staged activities, coordinated windows, validated media, and supported publisher/subscriber sequencing.',
   'DEMO TEST DATA — Validated registration, internal/PSTN dialing, voicemail, hunt pilots, route and translation patterns, SIP trunks, CER, and firmware behavior.',
   'DEMO TEST DATA — Completed user acceptance, remediated defects, documented known issues, updated support notes, and prepared operational handoff.'];
  i int; d date; week_start date; sheet uuid; task uuid; idx int; descr text;
BEGIN
  IF NOT EXISTS(SELECT 1 FROM app_users WHERE user_id=kevin AND is_active) THEN RAISE EXCEPTION 'Kevin test user unavailable'; END IF;
  IF NOT EXISTS(SELECT 1 FROM clients WHERE client_id=client) THEN RAISE EXCEPTION 'Summit test client unavailable'; END IF;
  IF EXISTS(SELECT 1 FROM billing_invoices WHERE project_id IN(q2,july)) THEN RAISE EXCEPTION 'Demo invoice exists; reseed blocked'; END IF;
  SELECT user_id INTO actor FROM app_users WHERE lower(email)='ahmed.adeyemi@onenecklab.com' AND is_active LIMIT 1;
  IF actor IS NULL THEN SELECT u.user_id INTO actor FROM app_users u JOIN app_user_role_assignments a ON a.user_id=u.user_id AND a.is_active JOIN app_roles r ON r.app_role_id=a.app_role_id AND r.is_active WHERE u.is_active AND r.role_code IN('SUPER_ADMINISTRATOR','ADMINISTRATOR','PROJECT_MANAGER','PROJECT_MANAGEMENT') ORDER BY u.created_at LIMIT 1; END IF;
  IF actor IS NULL THEN RAISE EXCEPTION 'No active admin or PM'; END IF;

  SELECT to_jsonb(p) INTO project_template FROM projects p WHERE p.client_id=client ORDER BY CASE WHEN upper(replace(coalesce(p.contract_type,''),'&','')) IN('TM','T M') THEN 0 ELSE 1 END,p.created_at DESC LIMIT 1;
  SELECT to_jsonb(t) INTO task_template FROM project_tasks t ORDER BY CASE WHEN coalesce(t.billable,true) THEN 0 ELSE 1 END,t.created_at DESC LIMIT 1;
  SELECT to_jsonb(a) INTO assignment_template FROM project_assignments a ORDER BY CASE WHEN a.user_id=kevin THEN 0 ELSE 1 END,a.created_at DESC NULLS LAST LIMIT 1;
  SELECT to_jsonb(s) INTO sheet_template FROM timesheets s ORDER BY CASE WHEN s.user_id=kevin THEN 0 ELSE 1 END,s.week_start_date DESC LIMIT 1;
  SELECT to_jsonb(e) INTO entry_template FROM time_entries e WHERE e.project_id IS NOT NULL AND e.task_id IS NOT NULL ORDER BY CASE WHEN e.billable THEN 0 ELSE 1 END,e.created_at DESC LIMIT 1;
  SELECT to_jsonb(c) INTO card_template FROM work_rate_cards c ORDER BY c.created_at DESC LIMIT 1;
  SELECT to_jsonb(l) INTO line_template FROM work_rate_card_lines l WHERE lower(coalesce(l.unit_type,''))='hour' ORDER BY l.created_at DESC LIMIT 1;
  IF project_template IS NULL OR task_template IS NULL OR assignment_template IS NULL OR sheet_template IS NULL OR entry_template IS NULL OR card_template IS NULL OR line_template IS NULL THEN RAISE EXCEPTION 'Required template row missing'; END IF;

  DELETE FROM time_entries WHERE project_id IN(q2,july);
  DELETE FROM project_assignments WHERE project_id IN(q2,july);
  DELETE FROM project_tasks WHERE project_id IN(q2,july);
  DELETE FROM project_purchase_orders WHERE project_id IN(q2,july);
  DELETE FROM project_billing_profiles WHERE project_id IN(q2,july);
  DELETE FROM projects WHERE project_id IN(q2,july);
  DELETE FROM work_rate_card_lines WHERE rate_card_id=card OR rate_line_id=line;
  DELETE FROM work_rate_cards WHERE rate_card_id=card;

  cjson:=card_template||jsonb_build_object('rate_card_id',card,'rate_card_code','DEMO-CUCM-TM-2026','rate_card_name','DEMO — CUCM T&M Hourly Rate','rate_card_type','client','client_id',client,'status','active','effective_start_date','2026-04-01'::date,'effective_end_date','2026-12-31'::date,'description','DEMO TEST DATA — CUCM engineering rate.','is_active',true,'created_at',now(),'updated_at',now());
  INSERT INTO work_rate_cards SELECT (jsonb_populate_record(NULL::work_rate_cards,cjson)).*;
  ljson:=line_template||jsonb_build_object('rate_line_id',line,'rate_card_id',card,'sku_code','DEMO-CUCM-COLLAB-ENG','display_name','DEMO Collaboration Engineer','description','DEMO TEST DATA — CUCM upgrade engineering services.','labor_category','Collaboration Engineer','time_type','normal','unit_type','hour','rate_amount',210.00,'billable_default',true,'is_active',true,'display_order',1,'created_at',now(),'updated_at',now());
  INSERT INTO work_rate_card_lines SELECT (jsonb_populate_record(NULL::work_rate_card_lines,ljson)).*;

  pjson:=project_template||jsonb_build_object('project_id',q2,'client_id',client,'project_code','DEMO-CUCM-Q2-2026','project_name','DEMO — CUCM 12.5 to 15.0 Upgrade — Q2 Utilization','description','DEMO TEST DATA — Closed Q2 project demonstrating 70 percent utilization; not actual labor.','status','completed','contract_type','TM','billable',true,'project_manager_user_id',actor,'project_coordinator_user_id',actor,'start_date','2026-04-01'::date,'end_date','2026-06-30'::date,'estimated_end_date','2026-06-30'::date,'closed_date','2026-06-30'::date,'sell_quote_number','DEMO-SELL-CUCM-Q2-2026','salesforce_id_number','DEMO-SF-CUCM-Q2-2026','certinia_id_number','DEMO-CERT-CUCM-Q2-2026','is_archived',false,'archive_reason','','created_at',now(),'updated_at',now());
  INSERT INTO projects SELECT (jsonb_populate_record(NULL::projects,pjson)).*;
  pjson:=project_template||jsonb_build_object('project_id',july,'client_id',client,'project_code','DEMO-CUCM-JUL-2026','project_name','DEMO — CUCM 12.5 to 15.0 Upgrade — July Partial Billing','description','DEMO TEST DATA — Active T&M project with 200 approved hours for partial invoicing; not actual labor.','status','active','contract_type','TM','billable',true,'project_manager_user_id',actor,'project_coordinator_user_id',actor,'start_date','2026-07-01'::date,'end_date','2026-12-31'::date,'estimated_end_date','2026-12-31'::date,'closed_date',NULL,'sell_quote_number','DEMO-SELL-CUCM-JUL-2026','salesforce_id_number','DEMO-SF-CUCM-JUL-2026','certinia_id_number','DEMO-CERT-CUCM-JUL-2026','is_archived',false,'archive_reason','','created_at',now(),'updated_at',now());
  INSERT INTO projects SELECT (jsonb_populate_record(NULL::projects,pjson)).*;

  INSERT INTO project_billing_profiles(project_id,default_rate_card_id,purchase_order_required,billing_instructions,created_by_user_id,updated_by_user_id,created_at,updated_at) VALUES
   (q2,card,false,'DEMO Q2 utilization project; do not invoice externally.',actor,actor,now(),now()),
   (july,card,true,'DEMO July project; use selected approved lines for partial invoice testing only.',actor,actor,now(),now());
  INSERT INTO project_purchase_orders(project_purchase_order_id,project_id,po_number,po_status,is_primary,authorized_amount,effective_start_date,effective_end_date,customer_reference,created_by_user_id,updated_by_user_id,created_at,updated_at)
   VALUES('04200000-0000-4000-8000-000000000205',july,'DEMO-PO-CUCM-2026-07','active',true,100000.00,'2026-07-01','2026-12-31','DEMO ONLY — CUCM partial-invoice showcase',actor,actor,now(),now());

  FOR i IN 1..10 LOOP
    task:=task_ids[i];
    tjson:=task_template||jsonb_build_object('task_id',task,'project_id',CASE WHEN i<=5 THEN q2 ELSE july END,'task_code',CASE WHEN i<=5 THEN format('DEMO-Q2-%s',lpad(i::text,2,'0')) ELSE format('DEMO-JUL-%s',lpad((i-5)::text,2,'0')) END,'task_name',names[i],'description',descriptions[i],'billable',true,'is_active',CASE WHEN i<=5 THEN false ELSE true END,'start_date',CASE WHEN i<=5 THEN '2026-04-01'::date ELSE '2026-07-01'::date END,'end_date',CASE WHEN i<=5 THEN '2026-06-30'::date ELSE '2026-12-31'::date END,'estimated_hours',CASE WHEN i<=5 THEN 72.8 ELSE 40 END,'allocated_hours',CASE WHEN i<=5 THEN 72.8 ELSE 40 END,'created_at',now(),'updated_at',now());
    INSERT INTO project_tasks SELECT (jsonb_populate_record(NULL::project_tasks,tjson)).*;
    ajson:=assignment_template||jsonb_build_object('project_assignment_id',(md5('DEMO-CUCM-ASSIGN-'||i::text))::uuid,'project_id',CASE WHEN i<=5 THEN q2 ELSE july END,'task_id',task,'user_id',kevin,'assigned_by_user_id',actor,'assignment_role','engineer','role_code','ENGINEERING','allocation_percent',CASE WHEN i<=5 THEN 70 ELSE 100 END,'allocated_hours',CASE WHEN i<=5 THEN 72.8 ELSE 40 END,'effective_start_date',CASE WHEN i<=5 THEN '2026-04-01'::date ELSE '2026-07-01'::date END,'effective_end_date',CASE WHEN i<=5 THEN '2026-06-30'::date ELSE '2026-12-31'::date END,'is_active',CASE WHEN i<=5 THEN false ELSE true END,'created_at',now(),'updated_at',now());
    INSERT INTO project_assignments SELECT (jsonb_populate_record(NULL::project_assignments,ajson)).*;
  END LOOP;

  FOR week_start IN
    SELECT DISTINCT (d::date-(extract(isodow from d)::int-1))::date FROM (SELECT d FROM generate_series('2026-04-01'::date,'2026-06-30'::date,'1 day') d WHERE extract(isodow from d) between 1 and 5 ORDER BY d LIMIT 52) x
    UNION SELECT DISTINCT (d::date-(extract(isodow from d)::int-1))::date FROM (SELECT d FROM generate_series('2026-07-01'::date,'2026-07-31'::date,'1 day') d WHERE extract(isodow from d)<>7 ORDER BY d LIMIT 25) y
  LOOP
    SELECT timesheet_id INTO sheet FROM timesheets WHERE user_id=kevin AND week_start_date=week_start LIMIT 1;
    IF sheet IS NULL THEN
      sheet:=(md5('DEMO-CUCM-TIMESHEET-'||week_start::text))::uuid;
      sjson:=sheet_template||jsonb_build_object('timesheet_id',sheet,'user_id',kevin,'week_start_date',week_start,'week_end_date',week_start+6,'status','manager_approved','submitted_at',now(),'approved_at',now(),'created_at',now(),'updated_at',now());
      INSERT INTO timesheets SELECT (jsonb_populate_record(NULL::timesheets,sjson)).*;
    END IF;
  END LOOP;

  i:=0;
  FOR d IN SELECT x::date FROM generate_series('2026-04-01'::date,'2026-06-30'::date,'1 day') x WHERE extract(isodow from x) between 1 and 5 ORDER BY x LIMIT 52 LOOP
    i:=i+1; idx:=((i-1)%5)+1; task:=task_ids[idx]; week_start:=d-(extract(isodow from d)::int-1);
    SELECT timesheet_id INTO sheet FROM timesheets WHERE user_id=kevin AND week_start_date=week_start LIMIT 1;
    descr:=descriptions[idx]||format(' Work date %s. DEMO entry %s of 52; 7.00 approved billable hours. Synthetic test data only.',d,i);
    ejson:=entry_template||jsonb_build_object('time_entry_id',(md5('DEMO-CUCM-Q2-'||d::text))::uuid,'timesheet_id',sheet,'user_id',kevin,'project_id',q2,'task_id',task,'non_project_time_category_id',NULL,'work_date',d,'hours',7.00,'billable',true,'time_type','normal','status','manager_approved','description',descr,'created_at',now(),'updated_at',now());
    INSERT INTO time_entries SELECT (jsonb_populate_record(NULL::time_entries,ejson)).*;
  END LOOP;

  i:=0;
  FOR d IN SELECT x::date FROM generate_series('2026-07-01'::date,'2026-07-31'::date,'1 day') x WHERE extract(isodow from x)<>7 ORDER BY x LIMIT 25 LOOP
    i:=i+1; idx:=((i-1)%5)+6; task:=task_ids[idx]; week_start:=d-(extract(isodow from d)::int-1);
    SELECT timesheet_id INTO sheet FROM timesheets WHERE user_id=kevin AND week_start_date=week_start LIMIT 1;
    descr:=descriptions[idx]||CASE WHEN extract(isodow from d)=6 THEN ' Saturday maintenance-window coordination and validation were included.' ELSE '' END||format(' Work date %s. DEMO entry %s of 25; 8.00 approved billable hours. Synthetic test data only.',d,i);
    ejson:=entry_template||jsonb_build_object('time_entry_id',(md5('DEMO-CUCM-JULY-'||d::text))::uuid,'timesheet_id',sheet,'user_id',kevin,'project_id',july,'task_id',task,'non_project_time_category_id',NULL,'work_date',d,'hours',8.00,'billable',true,'time_type','normal','status','manager_approved','description',descr,'created_at',now(),'updated_at',now());
    INSERT INTO time_entries SELECT (jsonb_populate_record(NULL::time_entries,ejson)).*;
  END LOOP;

  IF (SELECT sum(hours) FROM time_entries WHERE project_id=q2 AND user_id=kevin AND billable AND status='manager_approved')<>364 THEN RAISE EXCEPTION 'Q2 total mismatch'; END IF;
  IF (SELECT sum(hours) FROM time_entries WHERE project_id=july AND user_id=kevin AND billable AND status='manager_approved')<>200 THEN RAISE EXCEPTION 'July total mismatch'; END IF;
  IF (SELECT count(*) FROM work_rate_card_lines WHERE rate_card_id=card AND is_active AND billable_default AND lower(unit_type)='hour' AND lower(time_type)='normal')<>1 THEN RAISE EXCEPTION 'Rate-card ambiguity'; END IF;
END $do$;
COMMIT;

SELECT p.project_code,p.project_name,p.status,p.contract_type,count(DISTINCT pt.task_id) task_count,count(DISTINCT te.time_entry_id) entry_count,coalesce(sum(te.hours),0) approved_billable_hours
FROM projects p LEFT JOIN project_tasks pt ON pt.project_id=p.project_id LEFT JOIN time_entries te ON te.project_id=p.project_id AND te.user_id='faf19aa4-47b2-45ab-bc6d-0351ea5d72e0' AND te.billable AND te.status='manager_approved'
WHERE p.project_id IN('04200000-0000-4000-8000-000000000201','04200000-0000-4000-8000-000000000202') GROUP BY p.project_code,p.project_name,p.status,p.contract_type ORDER BY p.project_code;
SELECT 'Q2_TOTAL' metric,count(*) entries,sum(hours) hours FROM time_entries WHERE project_id='04200000-0000-4000-8000-000000000201' UNION ALL SELECT 'JULY_TOTAL',count(*),sum(hours) FROM time_entries WHERE project_id='04200000-0000-4000-8000-000000000202';
SELECT p.project_code,profile.purchase_order_required,po.po_number,card.rate_card_code,line.display_name,line.rate_amount FROM projects p LEFT JOIN project_billing_profiles profile ON profile.project_id=p.project_id LEFT JOIN project_purchase_orders po ON po.project_id=p.project_id AND po.is_primary AND po.po_status='active' LEFT JOIN work_rate_cards card ON card.rate_card_id=profile.default_rate_card_id LEFT JOIN work_rate_card_lines line ON line.rate_card_id=card.rate_card_id WHERE p.project_id IN('04200000-0000-4000-8000-000000000201','04200000-0000-4000-8000-000000000202') ORDER BY p.project_code;
SELECT count(*) invoices_created FROM billing_invoices WHERE project_id IN('04200000-0000-4000-8000-000000000201','04200000-0000-4000-8000-000000000202');
SELECT 'KEVIN_CUCM_DEMO_DATA_COMPLETE' status;
SELECT 'DATABASE_MODIFIED=YES' result;
SELECT 'INVOICE_CREATED=NO' result;
SELECT 'INVOICE_NUMBER_ALLOCATED=NO' result;
