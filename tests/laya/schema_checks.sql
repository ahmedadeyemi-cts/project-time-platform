BEGIN;
INSERT INTO celar_laya_decisions(decision_id,document_id,source_sha256,request_id,created_by,policy_version,evidence)
VALUES('00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000002',repeat('a',64),
'00000000-0000-0000-0000-000000000003','00000000-0000-0000-0000-000000000004',1,'{}');
INSERT INTO celar_laya_reviews(decision_id,reviewed_label,reviewed_by)
VALUES('00000000-0000-0000-0000-000000000001','invoice','00000000-0000-0000-0000-000000000004');
DO $$
DECLARE blocked boolean := false;
BEGIN
    BEGIN UPDATE celar_laya_decisions SET source_sha256=repeat('b',64);
    EXCEPTION WHEN SQLSTATE 'P0001' THEN blocked:=true; END;
    IF NOT blocked THEN RAISE EXCEPTION 'Decision update was not rejected'; END IF;
    blocked:=false;
    BEGIN DELETE FROM celar_laya_reviews;
    EXCEPTION WHEN SQLSTATE 'P0001' THEN blocked:=true; END;
    IF NOT blocked THEN RAISE EXCEPTION 'Review delete was not rejected'; END IF;
    blocked:=false;
    BEGIN INSERT INTO celar_laya_reviews(decision_id,reviewed_label,reviewed_by)
    VALUES('00000000-0000-0000-0000-000000000001','sow','00000000-0000-0000-0000-000000000004');
    EXCEPTION WHEN unique_violation THEN blocked:=true; END;
    IF NOT blocked THEN RAISE EXCEPTION 'Duplicate review was not rejected'; END IF;
    IF (SELECT enabled FROM celar_laya_settings) THEN RAISE EXCEPTION 'Must default disabled'; END IF;
END;
$$;
ROLLBACK;
SELECT 'LAYA_SCHEMA_CHECKS=PASS';
