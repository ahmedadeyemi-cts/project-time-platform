from pathlib import Path
root = Path(__file__).resolve().parents[2]
policy = (root/'src/backend/ProjectTime.Api/Ai/PulseAiExternalHttpsRuntimePolicy.cs').read_text()
assert policy.index('PulseAiExternalHttpsRuntimePolicy.RequireValid()') < policy.index('catch (Exception exception)')
assert 'exception is HttpRequestException' in policy
assert 'or TaskCanceledException' in policy
assert 'External HTTPS runtime configuration is present while the Test-only enable flag is false.' in policy
for name in ['Program.cs', 'Program.ScopedRbac.g.cs']:
    source = (root/'src/backend/ProjectTime.Api'/name).read_text()
    for route in ['/health/live', '/health/ready', '/health/dependencies']:
        assert f'app.MapGet("{route}"' in source
config = (root/'deployment/containers/web/default.conf.template').read_text()
assert 'location ^~ /health/' in config
print('Optional AI startup boundary and HTTP probe contracts passed')
