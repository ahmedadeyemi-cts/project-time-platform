#!/usr/bin/env python3
"""Validate package bytes, official schema, identity and personal-tab contract."""
import io
import json
import sys
import zipfile
from pathlib import Path
import jsonschema
from PIL import Image

with zipfile.ZipFile(sys.argv[1]) as archive:
    assert set(archive.namelist()) == {'manifest.json', 'color.png', 'outline.png'}
    manifest = json.loads(archive.read('manifest.json'))
    schema = json.loads(Path(sys.argv[2]).read_text())
    jsonschema.validators.validator_for(schema)(schema).validate(manifest)
    assert manifest['id'] != manifest['webApplicationInfo']['id']
    assert manifest['version'] == '1.0.2'
    assert manifest['staticTabs'][0]['entityId'] == 'pulse-notifications'
    assert manifest['staticTabs'][0]['scopes'] == ['personal']
    assert manifest['staticTabs'][0]['contentUrl'].endswith('/teams-notifications/index.html')
    assert {p['name'] for p in manifest['authorization']['permissions']['resourceSpecific']} == {'TeamsActivity.Send.User','TeamsAppInstallation.Read.User'}
    assert 'bots' not in manifest
    for name, size in [('color.png', (192,192)), ('outline.png',(32,32))]:
        image = Image.open(io.BytesIO(archive.read(name))); assert image.size == size
    outline=Image.open(io.BytesIO(archive.read('outline.png'))).convert('RGBA')
    assert outline.getchannel('A').getextrema()[0] == 0
    assert outline.getchannel('A').getextrema()[1] > 0
print('TEAMS_OFFICIAL_SCHEMA_AND_PACKAGE=PASS; TENANT_UPLOAD=NOT_PERFORMED')
