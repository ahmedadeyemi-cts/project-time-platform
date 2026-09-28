#!/usr/bin/env python3
"""Inventory and regression guard for business email fan-out, not a substitute for execution tests."""
import json
import re
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'src/backend/ProjectTime.Api'
raw = re.compile(r'new (?:System\.Net\.Mail\.)?SmtpClient\(|\.SendMailAsync\(|smtp\.Send\(|/sendMail|api\.brevo\.com/v3/smtp/email')
expected = {'Modules/Module065ProjectNotificationDelivery.cs': 3,
            'Modules/Module065AnalyticsAttachmentDelivery.cs': 3,
            'Program.cs': 1}
found = {}
for path in BASE.rglob('*.cs'):
    if 'obj' in path.parts or 'bin' in path.parts or path.name.endswith('.g.cs'):
        continue
    count = sum(bool(raw.search(line)) for line in path.read_text().splitlines())
    if count:
        found[str(path.relative_to(BASE))] = count
assert found == expected, f'Unreviewed email transport surface: {found}; update inventory and actual paired delivery tests.'
inventory = json.loads((ROOT/'tests/notification-parity/coverage.json').read_text())
for route in inventory['routes']:
    code = (ROOT/route['file']).read_text()
    assert route['email'] in code and route['mirror'] in code, route['family']+' has lost its paired channel.'
callers = {str(p.relative_to(ROOT)) for p in BASE.rglob('*.cs')
           if not p.name.endswith('.g.cs') and 'obj' not in p.parts and re.search(
               r'Module065(?:ProjectNotificationDelivery|AnalyticsAttachmentDelivery)\.DeliverAsync\(', p.read_text())}
assert callers == {item['file'] for item in inventory['routes'] if item['file'].endswith('.cs') and item['email'].startswith('Module065')}, f'Uninventoried native email sender: {callers}'
central = (BASE/'Modules/ProjectNotificationProcessingService.cs').read_text()
assert central.index('MicrosoftTeamsNotificationModule.TryDeliverDispatchAsync') < central.index('await Module065ProjectNotificationDelivery.DeliverAsync'), 'Teams depends on email transport result'
orchestration = (BASE/'Modules/EnterpriseNotificationOrchestrationService.cs').read_text()
assert 'await ProjectNotificationProcessingService.DeliverDispatchAsync' in orchestration and 'if (deliveryResult.Sent)' not in orchestration
expense = (BASE/'Modules/Module005ProjectExpenseMail.cs').read_text()
assert 'EnterpriseNotificationOrchestrationService.QueueExpenseUploadAsync' in expense and not raw.search(expense)
protocol = (BASE/'Modules/MicrosoftTeamsWorkflowProtocol.cs').read_text()
assert "var resource = audience.TrimEnd('/') + \"/\";" in protocol and 'var scope = resource + "/.default";' in protocol, 'Working OAuth audience changed'
assert protocol.index('authorizeBeforeSend(ct)') < protocol.index('new HttpRequestMessage(HttpMethod.Post, endpoint)'), 'No send-time source recheck'
outbox = (BASE/'Modules/MicrosoftTeamsNotificationOutbox.cs').read_text()
assert 'Module065ProjectNotificationDelivery.DeliverAsync' not in outbox and 'Module065AnalyticsAttachmentDelivery.DeliverAsync' not in outbox, 'Teams retry may resend email'
assert "AND status='failed' AND attempt_count=@expected" in outbox, 'Retry may replay an ambiguous or accepted result'
assert "status='outcome_unknown'" in outbox and 'pg_try_advisory_lock' in outbox and 'ON CONFLICT DO NOTHING RETURNING dispatch_id' in outbox
assert not re.search(r'FROM\s+(notification_outbox|email_notification_outbox|defect_notification_outbox)\b',outbox,re.I), 'Historical preview outbox drain introduced'
new_policies = (ROOT/'database/migrations/129_enterprise_reminder_delivery_sources.sql').read_text()
assert new_policies.count("'enterprise-reminder-v1','scanner',FALSE)")==4 and new_policies.count("'test_only'")==4
assert 'ON CONFLICT(policy_code) DO NOTHING' in new_policies
program = (BASE/'Program.cs').read_text()
assert program.count('SendProjectPulseEmailThroughSharedProviderAsync(')==3, 'Legacy email caller inventory changed; pair real sends, never implicitly mirror a provider test'
assert 'governed_dispatch_required' in program and 'Module065WorkRegisterNotificationBridge.DeliverAsync' in program
assert 'directSmtpAuthorized = false' in orchestration
print(f'EMAIL_TEAMS_INVENTORY_ROUTES={len(inventory["routes"])}; RAW_TRANSPORT_OWNERS={len(found)}; NEW_OPT_IN_SOURCES=4; RESULT=PASS')
