-- Non-destructive rollback: stop only the new producer policies and retain event/audit evidence.
BEGIN;
UPDATE enterprise_notification_policies SET enabled=FALSE,delivery_boundary='locked',updated_at=now()
WHERE producer_contract='enterprise-reminder-v1' AND policy_code IN
 ('TIME_NOT_SUBMITTED','TIME_NOT_SUBMITTED_ESCALATION','COMPANY_HOLIDAY_UPCOMING','PM_MONTH_END_REMINDER');
COMMIT;
