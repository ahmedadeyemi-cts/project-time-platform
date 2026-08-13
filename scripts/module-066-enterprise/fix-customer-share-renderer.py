#!/usr/bin/env python3
from pathlib import Path

validator_path = Path('src/frontend/project-time-web/scripts/validate-module-066-project-flowhive.mjs')
validator = validator_path.read_text(encoding='utf-8')
for old, new in {
    "frontend.includes('<th>Progress</th>')": "frontend.includes('>Progress</th>')",
    "frontend.includes('<th>Type</th>')": "frontend.includes('>Type</th>')",
}.items():
    if old not in validator:
        raise SystemExit(f'Missing expected legacy validator marker: {old}')
    validator = validator.replace(old, new, 1)
validator_path.write_text(validator, encoding='utf-8')

backend_path = Path('src/backend/ProjectTime.Api/Modules/ProjectFlowHiveEnterpriseModule.cs')
backend = backend_path.read_text(encoding='utf-8')
start_marker = '        return $"""\n            <!doctype html>'
end_marker = '            """;\n    }\n\n    private static async Task<OpenOutcome>'
start = backend.find(start_marker)
end = backend.find(end_marker, start)
if start < 0 or end < 0:
    raise SystemExit('Customer-share renderer raw-string anchors were not found.')
end += len('            """;')
replacement = '''        var projectStart = H(schedule.ProjectStartDate?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture));
        var projectFinish = H(schedule.ProjectFinishDate?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture));
        var customerNote = string.IsNullOrWhiteSpace(note)
            ? string.Empty
            : $"<p><strong>Project Manager note:</strong> {H(note)}</p>";
        return """
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>__PROJECT_CODE__ Project Status</title>
            <style>
            :root{--navy:#082b4c;--blue:#057aa8;--ink:#17324a;--muted:#617286;--line:#d8e2ea;--soft:#f3f8fb}
            *{box-sizing:border-box}body{margin:0;font-family:Inter,Segoe UI,Arial,sans-serif;color:var(--ink);background:var(--soft)}
            main{max-width:1180px;margin:32px auto;padding:0 20px}header{padding:28px;border-radius:18px;color:#fff;background:linear-gradient(135deg,#061d35,#0b5276)}
            .brand{font-weight:900;letter-spacing:.08em;text-transform:uppercase;color:#82ddf6}h1{margin:.35rem 0 .25rem}.meta{display:flex;gap:18px;flex-wrap:wrap;color:#d9edf7}
            section{margin-top:18px;padding:22px;border:1px solid var(--line);border-radius:16px;background:#fff;box-shadow:0 8px 24px rgba(7,35,59,.07)}
            h2{margin-top:0;color:var(--navy)}p{line-height:1.55}table{width:100%;border-collapse:collapse;font-size:14px}th{text-align:left;background:var(--navy);color:#fff;padding:11px}td{padding:11px;border-bottom:1px solid var(--line);vertical-align:top}td small{display:block;margin-top:4px;color:var(--muted);line-height:1.4}footer{padding:20px 0;color:var(--muted);font-size:12px}@media(max-width:760px){table{display:block;overflow:auto}}
            </style></head><body><main>
            <header><div class="brand">US Signal Project FlowHive</div><h1>__PROJECT_CODE__ · __PROJECT_NAME__</h1><div class="meta"><span>Customer: __CUSTOMER__</span><span>Reviewed project baseline</span><span>Link expires __EXPIRES__</span></div></header>
            <section><h2>Executive summary</h2><p>__SUMMARY__</p>__CUSTOMER_NOTE__</section>
            <section><h2>Reviewed schedule</h2><p>__PROJECT_START__ through __PROJECT_FINISH__ · __CRITICAL_COUNT__ critical task(s)</p>
            <table><thead><tr><th>WBS</th><th>Task</th><th>Start</th><th>Finish</th><th>Progress</th><th>Status</th></tr></thead><tbody>__ROWS__</tbody></table></section>
            <footer>Customer-safe, read-only Project FlowHive view. Internal notes, private citations, assignments, financial details, and provider data are not included.</footer>
            </main></body></html>
            """
            .Replace("__PROJECT_CODE__", H(projectCode), StringComparison.Ordinal)
            .Replace("__PROJECT_NAME__", H(projectName), StringComparison.Ordinal)
            .Replace("__CUSTOMER__", H(customer), StringComparison.Ordinal)
            .Replace("__EXPIRES__", expiresAt.ToString("MMM d, yyyy", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__SUMMARY__", H(summary), StringComparison.Ordinal)
            .Replace("__CUSTOMER_NOTE__", customerNote, StringComparison.Ordinal)
            .Replace("__PROJECT_START__", projectStart, StringComparison.Ordinal)
            .Replace("__PROJECT_FINISH__", projectFinish, StringComparison.Ordinal)
            .Replace("__CRITICAL_COUNT__", schedule.CriticalTaskCount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__ROWS__", rows.ToString(), StringComparison.Ordinal);'''
backend = backend[:start] + replacement + backend[end:]
backend_path.write_text(backend, encoding='utf-8')
print('FLOWHIVE_CUSTOMER_SHARE_RENDERER_FIX=PASS')
