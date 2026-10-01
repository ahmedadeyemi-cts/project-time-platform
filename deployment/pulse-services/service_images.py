"""Build and publish the exact scanned service images within protected Test.
No account or application secrets are needed by this process.
"""
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
from test_resources import ACR,COMPONENTS,validate_images,APPS,GROUP,API,runtime_identity_isolated
ROOT=Path(__file__).resolve().parents[2]

def require(condition,code):
    if not condition:raise ValueError(code)

def command(args):
    result=subprocess.run(args,cwd=ROOT,capture_output=True,text=True,timeout=1500)
    require(result.returncode==0,'image_operation_failed')
    return result.stdout.strip()

def fingerprint():
    names=command(['git','ls-files','deployment/pulse-services','deployment/pulse-document-processing']).splitlines()
    data=[]
    for name in names:
        p=ROOT/name;require(p.is_file() and not p.is_symlink(),'invalid_source_file')
        data.append(name+' '+hashlib.sha256(p.read_bytes()).hexdigest())
    return hashlib.sha256(('\n'.join(data)+'\n').encode()).hexdigest()

def context():
    source=os.environ.get('PULSE_SOURCE_SHA','')
    require(re.fullmatch('[0-9a-f]{40}',source) is not None,'invalid_source')
    require(os.environ.get('GITHUB_REPOSITORY')=='ahmedadeyemi-cts/project-time-platform'
        and os.environ.get('GITHUB_REF')=='refs/heads/main'
        and os.environ.get('GITHUB_RUN_ATTEMPT')=='1','protected_main_required')
    require(command(['git','rev-parse','HEAD'])==source,'checkout_source_changed')
    root=Path(os.environ['RUNNER_TEMP']).resolve()
    safe=Path(os.environ['PULSE_SAFE_EVIDENCE'])
    require(not safe.is_symlink() and safe.resolve()==root/'pulse-services-safe','unsafe_evidence_directory')
    safe.mkdir(mode=0o700,exist_ok=True);safe.chmod(0o700)
    return source,safe

def inspect(component):
    rows=json.loads(command(['docker','image','inspect','pulse-services-'+component+':cutover']))
    require(len(rows)==1,'image_inventory')
    return rows[0]

def scan_verified(report,identity):
    require(isinstance(report,dict) and report.get('SchemaVersion')==2,'scan_report_invalid')
    require(report.get('Metadata',{}).get('ImageID')==identity,'scan_image_identity_mismatch')
    require(report.get('ArtifactType')=='container_image','wrong_scan_type')
    rows=report.get('Results',[]);require(isinstance(rows,list) and rows,'scan_results_missing')
    for row in rows:
        for finding in row.get('Vulnerabilities',[]):
            require(not (finding.get('Severity') in ('HIGH','CRITICAL') and finding.get('FixedVersion')),'blocking_image_vulnerability')
    return True

def installed_selection():
    data=json.loads(command(['az','containerapp','show','-g',GROUP,'-n',API,'--query',
        "properties.template.containers[0].env[?name=='PROJECTPULSE_DOCUMENT_SERVICE_MODE'||name=='PROJECTPULSE_LAYA_SERVICE_MODE'].{name:name,value:value}",'-o','json','--only-show-errors']))
    values={x['name']:x.get('value') for x in data}
    modes=[values.get(p+'MODE','legacy') for p in ('PROJECTPULSE_DOCUMENT_SERVICE_','PROJECTPULSE_LAYA_SERVICE_')]
    require(modes in (['legacy','legacy'],['pulse_container','pulse_container']),'mixed_service_selection')
    return modes[0]=='pulse_container'

def existing_images(fp):
    images={}
    for kind,name in APPS.items():
        app=json.loads(command(['az','containerapp','show','-g',GROUP,'-n',name,'-o','json','--only-show-errors']))
        p=app['properties'];c=p['configuration'];tags=app.get('tags',{})
        require(tags.get('managedBy')=='pulse-services-reviewed-cutover' and tags.get('serviceFingerprint')==fp,'service_upgrade_requires_review')
        require(c['ingress']['external'] is False and c['ingress']['allowInsecure'] is False
            and runtime_identity_isolated(c),'existing_service_boundary_changed')
        require(p['latestRevisionName']==p['latestReadyRevisionName'],'existing_service_not_ready')
        for container in p['template']['containers']:
            name=container['name']
            if name=='signature-updater':
                require(container['image']==next(x['image'] for x in p['template']['containers'] if x['name']=='scanner'),'signature_image_mismatch')
            else:
                component='laya' if name=='laya-model' else name
                require(component in COMPONENTS and component not in images,'component_inventory_changed')
                images[component]=container['image']
    validate_images(images);return images

def build():
    source,safe=context();fp=fingerprint();ids={}
    selected=installed_selection();installed=existing_images(fp) if selected else None
    for component in sorted(COMPONENTS):
        if selected:
            command(['docker','pull',installed[component]])
            command(['docker','tag',installed[component],'pulse-services-'+component+':cutover'])
            ids[component]=inspect(component)['Id'];continue
        command(['docker','build','--label','org.opencontainers.image.revision='+source,
                 '--label','io.pulse.services.source-fingerprint='+fp,'-t','pulse-services-'+component+':cutover',
                 '-f','deployment/pulse-services/Dockerfile.'+component,'.'])
        meta=inspect(component)
        require(meta['Config']['User']=='65534:65534','nonroot_runtime_required')
        require(meta['Config']['Labels'].get('org.opencontainers.image.revision')==source,'image_source_mismatch')
        ids[component]=meta['Id'];print('PULSE_IMAGE_BUILT='+component,flush=True)
    (safe/'build-identities.json').write_text(json.dumps({'source':source,'fingerprint':fp,'images':ids,'mode':'verify_existing' if selected else 'initial','installedImages':installed},indent=2)+'\n')

def publish():
    source,safe=context();receipt=json.loads((safe/'build-identities.json').read_text())
    require(receipt['source']==source and receipt['fingerprint']==fingerprint(),'build_source_mismatch')
    for component in sorted(COMPONENTS):
        meta=inspect(component);require(meta['Id']==receipt['images'][component],'image_changed_after_build')
        scan_verified(json.loads((safe/(component+'-scan.json')).read_text()),meta['Id'])
    if receipt.get('mode')=='verify_existing':
        require(installed_selection() and existing_images(receipt['fingerprint'])==receipt['installedImages'],'existing_services_changed')
        (safe/'registry-images.json').write_text(json.dumps(receipt['installedImages'],indent=2)+'\n')
        print('PULSE_EXISTING_SERVICE_IMAGES_RESCANNED=PASS',flush=True);return
    command(['az','acr','login','--name',ACR.split('.')[0],'--only-show-errors'])
    images={};run=os.environ['GITHUB_RUN_ID'];require(re.fullmatch('[1-9][0-9]{0,19}',run),'invalid_run')
    for component in sorted(COMPONENTS):
        tag='pulse-services-'+component+':svc-'+run
        remote=ACR+'/'+tag
        command(['docker','tag','pulse-services-'+component+':cutover',remote]);command(['docker','push',remote])
        digest=command(['az','acr','repository','show','-n',ACR.split('.')[0],'--image',tag,'--query','digest','-o','tsv','--only-show-errors'])
        require(re.fullmatch('sha256:[0-9a-f]{64}',digest),'registry_digest_missing')
        immutable=ACR+'/pulse-services-'+component+'@'+digest
        command(['docker','pull',immutable]);remote_meta=json.loads(command(['docker','image','inspect',immutable]))[0]
        require(remote_meta['Id']==receipt['images'][component],'published_image_different_from_scanned')
        images[component]=immutable
    validate_images(images)
    (safe/'registry-images.json').write_text(json.dumps(images,indent=2)+'\n')
    print('PULSE_SCANNED_IMMUTABLE_IMAGES=VERIFIED',flush=True)

if __name__=='__main__':
    try:
        require(len(sys.argv)==2 and sys.argv[1] in ('build','publish'),'invalid_phase')
        globals()[sys.argv[1]]()
    except Exception as e:
        code=str(e) if isinstance(e,ValueError) and re.fullmatch('[a-z_]+',str(e)) else type(e).__name__
        print('PULSE_IMAGE_PHASE_FAILED='+code);raise SystemExit(1)
