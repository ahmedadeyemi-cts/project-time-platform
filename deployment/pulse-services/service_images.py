"""Build and publish the exact scanned service images within protected Test.
No account or application secrets are needed by this process.
"""
import hashlib,json,os,re,subprocess,sys,time
from pathlib import Path
from test_resources import ACR,COMPONENTS,validate_images,APPS,GROUP,API,runtime_identity_isolated
ROOT=Path(__file__).resolve().parents[2]
FINGERPRINT_PATHS=('deployment/pulse-services','deployment/pulse-document-processing')
CONTROL_ONLY_COMPATIBLE_PATHS=frozenset({
    'deployment/pulse-services/post_activation_acceptance.py',
    'deployment/pulse-services/service_images.py',
})

def require(condition,code):
    if not condition:raise ValueError(code)

def command(args):
    # Export fixed codes only. Never include command arguments, credentials,
    # registry responses or raw stderr in an exception or published receipt.
    prefixes = ((['docker','push'],'docker_push'), (['docker','pull'],'docker_pull'),
        (['docker','image','inspect'],'docker_inspect'), (['docker','build'],'docker_build'),
        (['docker','tag'],'docker_tag'), (['az','acr','login'],'registry_login'),
        (['az','acr','manifest','show'],'registry_manifest'),
        (['az','acr','repository','show'],'registry_metadata'))
    operation = next((name for prefix,name in prefixes if args[:len(prefix)]==prefix),'operation')
    try:
        result=subprocess.run(args,cwd=ROOT,capture_output=True,text=True,timeout=1500)
    except subprocess.TimeoutExpired:
        raise ValueError('image_'+operation+'_timeout') from None
    if result.returncode != 0:
        text=(result.stderr or '')[-8192:].lower()
        patterns=(('unauthorized',('unauthorized','authentication required','authorizationfailed','denied:')),
            ('disk_full',('no space left on device','disk quota exceeded')),
            ('not_visible',('manifest unknown','manifest_unknown','name_unknown','not found')),
            ('throttled',('too many requests','toomanyrequests','429')),
            ('transport',('connection reset','connection refused','i/o timeout','tls handshake timeout')))
        category=next((name for name,terms in patterns if any(term in text for term in terms)),'failed')
        raise ValueError('image_'+operation+'_'+category)
    return result.stdout.strip()

def read_registry(args):
    require(args[:4] in (['az','acr','manifest','show'],['az','acr','repository','show']),
        'registry_retry_must_be_read_only')
    for attempt in range(3):
        try:
            return command(args)
        except ValueError as error:
            if attempt==2 or not str(error).endswith(('_not_visible','_transport','_timeout','_throttled')):
                raise
            time.sleep(attempt+1)

def manifest_verified(manifest,identity):
    # The immutable registry manifest's config digest is the scanned image ID.
    # That configuration also commits to the rootfs diff IDs. Azure will pull
    # the content-addressed layers; a second local unpack is not an identity test.
    require(isinstance(manifest,dict) and type(manifest.get('schemaVersion')) is int
        and manifest['schemaVersion']==2 and 'manifests' not in manifest,'registry_manifest_invalid')
    require(manifest.get('mediaType') in ('application/vnd.docker.distribution.manifest.v2+json',
        'application/vnd.oci.image.manifest.v1+json'),'registry_manifest_type_invalid')
    require(re.fullmatch('sha256:[0-9a-f]{64}',identity or '') is not None,'scanned_identity_invalid')
    config=manifest.get('config')
    require(isinstance(config,dict) and config.get('digest')==identity
        and type(config.get('size')) is int and 0<config['size']<=1048576
        and not config.get('urls') and config.get('mediaType') in
        ('application/vnd.docker.container.image.v1+json','application/vnd.oci.image.config.v1+json'),
        'published_image_different_from_scanned')
    layers=manifest.get('layers')
    require(isinstance(layers,list) and 1<=len(layers)<=256,'registry_layers_invalid')
    for layer in layers:
        require(isinstance(layer,dict) and isinstance(layer.get('digest'),str)
            and re.fullmatch('sha256:[0-9a-f]{64}',layer['digest']) is not None
            and type(layer.get('size')) is int and 0<layer['size']<=21474836480
            and not layer.get('urls') and layer.get('mediaType') in
            ('application/vnd.docker.image.rootfs.diff.tar.gzip','application/vnd.oci.image.layer.v1.tar',
             'application/vnd.oci.image.layer.v1.tar+gzip','application/vnd.oci.image.layer.v1.tar+zstd'),
            'registry_layer_descriptor_invalid')
    return True

def _snapshot_current():
    names=command(['git','ls-files',*FINGERPRINT_PATHS]).splitlines()
    hashes={}
    for name in names:
        p=ROOT/name
        require(p.is_file() and not p.is_symlink(),'invalid_source_file')
        hashes[name]=hashlib.sha256(p.read_bytes()).hexdigest()
    digest=hashlib.sha256(('\n'.join(name+' '+hashes[name] for name in names)+'\n').encode()).hexdigest()
    return digest,hashes

def _reviewed_snapshot(source):
    require(re.fullmatch('[0-9a-f]{40}',source or '') is not None,'service_source_invalid')
    listing=subprocess.run(['git','ls-tree','-r','--name-only',source,'--',*FINGERPRINT_PATHS],
        cwd=ROOT,capture_output=True,text=True,timeout=60)
    require(listing.returncode==0,'service_source_history_unavailable')
    names=[name for name in listing.stdout.splitlines() if name]
    hashes={}
    for name in names:
        require(name.startswith(FINGERPRINT_PATHS) and '..' not in Path(name).parts,'service_source_path_invalid')
        blob=subprocess.run(['git','show',source+':'+name],cwd=ROOT,capture_output=True,timeout=60)
        require(blob.returncode==0,'service_source_history_unavailable')
        hashes[name]=hashlib.sha256(blob.stdout).hexdigest()
    digest=hashlib.sha256(('\n'.join(name+' '+hashes[name] for name in names)+'\n').encode()).hexdigest()
    return digest,hashes

def fingerprint():
    return _snapshot_current()[0]

def reviewed_installed_source(tags):
    if not isinstance(tags,dict):
        return False
    source=tags.get('source','')
    installed_fingerprint=tags.get('serviceFingerprint','')
    if re.fullmatch('[0-9a-f]{40}',source or '') is None or re.fullmatch('[0-9a-f]{64}',installed_fingerprint or '') is None:
        return False
    ancestry=subprocess.run(['git','merge-base','--is-ancestor',source,'HEAD'],cwd=ROOT,
        stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=60)
    if ancestry.returncode!=0:
        return False
    try:
        reviewed_fingerprint,_=_reviewed_snapshot(source)
    except (ValueError,subprocess.SubprocessError,OSError):
        return False
    return reviewed_fingerprint==installed_fingerprint

def reviewed_control_only_compatible(tags,current_fingerprint):
    if not isinstance(tags,dict):
        return False
    installed_fingerprint=tags.get('serviceFingerprint','')
    if installed_fingerprint==current_fingerprint:
        return True
    if not reviewed_installed_source(tags):
        return False
    source=tags.get('source','')
    try:
        _,reviewed_hashes=_reviewed_snapshot(source)
        current_verified,current_hashes=_snapshot_current()
    except (ValueError,subprocess.SubprocessError,OSError):
        return False
    if current_verified!=current_fingerprint or set(reviewed_hashes)!=set(current_hashes):
        return False
    changed={name for name in current_hashes if current_hashes[name]!=reviewed_hashes[name]}
    return bool(changed) and changed.issubset(CONTROL_ONLY_COMPATIBLE_PATHS)

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

def existing_images(fp,*,allow_upgrade=False):
    images={};provenance=[]
    for kind,app_name in APPS.items():
        app=json.loads(command(['az','containerapp','show','-g',GROUP,'-n',app_name,'-o','json','--only-show-errors']))
        p=app['properties'];c=p['configuration'];tags=app.get('tags',{})
        require(tags.get('managedBy')=='pulse-services-reviewed-cutover','service_upgrade_requires_review')
        if allow_upgrade:
            require(reviewed_installed_source(tags),'service_upgrade_requires_review')
        else:
            require(reviewed_control_only_compatible(tags,fp),'service_upgrade_requires_review')
        source=tags.get('source','');run=tags.get('deploymentRun','');installed_fp=tags.get('serviceFingerprint','')
        require(re.fullmatch('[0-9a-f]{40}',source or '') is not None and re.fullmatch('[1-9][0-9]{0,19}',run or '') is not None
            and re.fullmatch('[0-9a-f]{64}',installed_fp or '') is not None,'service_upgrade_requires_review')
        provenance.append((source,run,installed_fp))
        require(c['ingress']['external'] is False and c['ingress']['allowInsecure'] is False
            and runtime_identity_isolated(c),'existing_service_boundary_changed')
        require(p['latestRevisionName']==p['latestReadyRevisionName'],'existing_service_not_ready')
        for container in p['template']['containers']:
            container_name=container['name']
            if container_name=='signature-updater':
                require(container['image']==next(x['image'] for x in p['template']['containers'] if x['name']=='scanner'),'signature_image_mismatch')
            else:
                component='laya' if container_name=='laya-model' else container_name
                require(component in COMPONENTS and component not in images,'component_inventory_changed')
                images[component]=container['image']
    require(len(set(provenance))==1,'service_upgrade_provenance_mismatch')
    validate_images(images);return images

def build():
    source,safe=context();fp=fingerprint();ids={}
    selected=installed_selection();installed=None;mode='initial'
    if selected:
        try:
            installed=existing_images(fp);mode='verify_existing'
        except ValueError as error:
            if str(error)!='service_upgrade_requires_review':raise
            installed=existing_images(fp,allow_upgrade=True);mode='upgrade_existing'
    for component in sorted(COMPONENTS):
        if mode=='verify_existing':
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
    (safe/'build-identities.json').write_text(json.dumps({'source':source,'fingerprint':fp,'images':ids,'mode':mode,'installedImages':installed},indent=2)+'\n')

def _publish(source,safe):
    receipt=json.loads((safe/'build-identities.json').read_text())
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
        digest=read_registry(['az','acr','repository','show','-n',ACR.split('.')[0],'--image',tag,'--query','digest','-o','tsv','--only-show-errors'])
        require(re.fullmatch('sha256:[0-9a-f]{64}',digest),'registry_digest_missing')
        immutable=ACR+'/pulse-services-'+component+'@'+digest
        manifest=json.loads(read_registry(['az','acr','manifest','show','-r',ACR.split('.')[0],
            '-n','pulse-services-'+component+'@'+digest,'-o','json','--only-show-errors']))
        manifest_verified(manifest,receipt['images'][component])
        images[component]=immutable
        (safe/'publication-progress.json').write_text(json.dumps({'source':source,'run':run,
            'status':'in_progress','verifiedComponents':sorted(images),'rawOutputPublished':False},indent=2)+'\n')
        print('PULSE_REGISTRY_IMAGE_VERIFIED='+component,flush=True)
    validate_images(images)
    (safe/'registry-images.json').write_text(json.dumps(images,indent=2)+'\n')
    print('PULSE_SCANNED_IMMUTABLE_IMAGES=VERIFIED',flush=True)

def publish():
    source,safe=context()
    try:
        _publish(source,safe)
        (safe/'publication-status.json').write_text(json.dumps({'source':source,'status':'passed',
            'rawOutputPublished':False})+'\n')
    except Exception as error:
        code=str(error) if isinstance(error,ValueError) and re.fullmatch('[a-z_]{1,100}',str(error)) else type(error).__name__
        (safe/'publication-status.json').write_text(json.dumps({'source':source,'status':'failed',
            'diagnostic':code,'rawOutputPublished':False})+'\n')
        raise

if __name__=='__main__':
    try:
        require(len(sys.argv)==2 and sys.argv[1] in ('build','publish'),'invalid_phase')
        globals()[sys.argv[1]]()
    except Exception as e:
        code=str(e) if isinstance(e,ValueError) and re.fullmatch('[a-z_]+',str(e)) else type(e).__name__
        print('PULSE_IMAGE_PHASE_FAILED='+code);raise SystemExit(1)
