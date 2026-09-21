-- Run with psql -v runtime_role=<approved application database role>.
-- Identifier quoting is handled by PostgreSQL format(%I), never interpolation.
SELECT format('GRANT SELECT, UPDATE ON public.celar_laya_settings TO %I', :'runtime_role') \gexec
SELECT format('GRANT SELECT, INSERT ON public.celar_laya_settings_audit, public.celar_laya_decisions, public.celar_laya_reviews TO %I', :'runtime_role') \gexec
