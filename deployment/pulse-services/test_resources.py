"""Pure resource descriptions for two services in the existing Test environment.
No SDK or subprocess calls occur in this module. Every runtime remains non-root.
"""
import hashlib,re
SUB='cd32baeb-7b71-4bc0-8ea3-9f23a50903fe'
GROUP='rg-project-health-dashboard-test-app-westus3'
ROOT=f'/subscriptions/{SUB}/resourceGroups/{GROUP}'
ENV=ROOT+'/providers/Microsoft.App/managedEnvironments/cae-phd-test-westus3'
DOMAIN='jollywave-6212cd8b.westus3.azurecontainerapps.io'
ACR='acrphdtest7825cc.azurecr.io'
IDENTITY=ROOT+'/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-phd-test-app-westus3'
API='ca-phd-test-api-westus3'
APPS={'documents':'ca-phd-test-documents-westus3','laya':'ca-phd-test-laya-westus3'}
STORAGE='pulse-antivirus-signatures'
COMPONENTS={'documents','scanner','laya-gateway','laya'}
def require(value,reason):
    if not value:raise ValueError(reason)
def validate_images(images):
    require(set(images)==COMPONENTS,'image_inventory')
    for name,value in images.items():
        require(re.fullmatch(re.escape(ACR+'/pulse-services-'+name+'@sha256:')+r'[0-9a-f]{64}',value) is not None,'image_identity')
def configuration(kind,token,secret,approval):
    require(kind in APPS,'service_scope')
    require(re.fullmatch(r'[A-Za-z0-9_-]{32,4096}',token) is not None,'service_credential')
    prefix='PROJECTPULSE_DOCUMENT_SERVICE_' if kind=='documents' else 'PROJECTPULSE_LAYA_SERVICE_'
    values={'MODE':'pulse_container','ENVIRONMENT_DOMAIN':DOMAIN,'ORIGIN':'https://'+APPS[kind]+'.internal.'+DOMAIN,
            'APPROVAL_REFERENCE':approval,'TOKEN_SECRET_REFERENCE':secret}
    first='pulse-documents-v1' if kind=='documents' else 'pulse-laya-v1'
    identity='\n'.join([first,'pulse_container','test',DOMAIN,values['ORIGIN'],approval,secret,hashlib.sha256(token.encode()).hexdigest()])
    values['CONFIGURATION_SHA256']=hashlib.sha256(identity.encode()).hexdigest()
    return [{'name':prefix+k,'value':v} for k,v in values.items()]+[{'name':prefix+'TOKEN','secretRef':secret}]
def application(kind,images,token,secret,sha,run):
    require(kind in APPS,'service_scope');validate_images(images)
    require(re.fullmatch(r'[0-9a-f]{40}',sha) is not None and str(run).isdigit(),'release_identity')
    require(re.fullmatch(r'[A-Za-z0-9_-]{32,4096}',token) is not None,'service_credential')
    socket='/run/clamav' if kind=='documents' else '/run/celar-laya'
    mount={'volumeName':'local-socket','mountPath':socket}
    gateway='documents' if kind=='documents' else 'laya-gateway'
    proxy={'name':gateway,'image':images[gateway],'resources':{'cpu':0.5 if kind=='documents' else 0.25,'memory':'1Gi' if kind=='documents' else '0.5Gi'},
           'env':[{'name':'PULSE_SERVICE_TOKEN','secretRef':secret}], 'volumeMounts':[mount],
           'probes':[{'type':'Startup','httpGet':{'path':'/health/live','port':8082},'periodSeconds':10,'failureThreshold':10,'timeoutSeconds':5},
                     {'type':'Readiness','httpGet':{'path':'/health/ready','port':8082},'periodSeconds':15,'failureThreshold':3,'timeoutSeconds':8},
                     {'type':'Liveness','httpGet':{'path':'/health/live','port':8082},'periodSeconds':30,'failureThreshold':3,'timeoutSeconds':5}]}
    containers=[proxy];volumes=[{'name':'local-socket','storageType':'EmptyDir'}]
    if kind=='documents':
        sig={'volumeName':'signatures','mountPath':'/var/lib/clamav'}
        containers.extend([{'name':'scanner','image':images['scanner'],'resources':{'cpu':2,'memory':'4Gi'},'volumeMounts':[mount,sig]},
            {'name':'signature-updater','image':images['scanner'],'command':['python3','/opt/pulse-services/runtime.py','updater'],
             'resources':{'cpu':1,'memory':'2Gi'},'volumeMounts':[mount,sig]}])
        volumes.append({'name':'signatures','storageName':STORAGE,'storageType':'AzureFile','mountOptions':'uid=65534,gid=65534,dir_mode=0750,file_mode=0640,nosuid,nodev,noexec'})
    else:containers.append({'name':'laya-model','image':images['laya'],'resources':{'cpu':2,'memory':'4Gi'},'volumeMounts':[mount]})
    resource={'location':'westus3','identity':{'type':'UserAssigned','userAssignedIdentities':{IDENTITY:{}}},
        'tags':{'owner':'Pulse','environment':'test','source':sha,'managedBy':'pulse-services-reviewed-cutover','deploymentRun':str(run)},
        'properties':{'environmentId':ENV,'workloadProfileName':'Consumption','configuration':{
            'activeRevisionsMode':'Single','maxInactiveRevisions':2,'identitySettings':[{'identity':IDENTITY,'lifecycle':'None'}],
            'secrets':[{'name':secret,'value':token}],'registries':[{'server':ACR,'identity':IDENTITY}],
            'ingress':{'external':False,'targetPort':8082,'transport':'http','allowInsecure':False}},
            'template':{'revisionSuffix':'svc-'+str(run),'containers':containers,'volumes':volumes,
                        'scale':{'minReplicas':1,'maxReplicas':1},'terminationGracePeriodSeconds':30}}}
    validate_resource(resource,kind,images)
    return resource
def runtime_identity_isolated(configuration):
    settings=configuration.get('identitySettings',[])
    return len(settings)==1 and settings[0].get('identity','').lower()==IDENTITY.lower() and settings[0].get('lifecycle')=='None'
def validate_resource(body,kind,images):
    p=body['properties'];c=p['configuration'];t=p['template']
    require(kind in APPS and p['environmentId']==ENV and body['location']=='westus3','wrong_environment')
    require(c['ingress']['external'] is False and c['ingress']['allowInsecure'] is False and c['ingress']['targetPort']==8082,'ingress_boundary')
    require(not c['ingress'].get('additionalPortMappings'),'raw_ports')
    require(runtime_identity_isolated(c),'runtime_identity_exposed')
    require(t['scale']=={'minReplicas':1,'maxReplicas':1},'scale_boundary')
    require(sum(x['resources']['cpu'] for x in t['containers'])<=4,'cpu_boundary')
    require(sum(float(x['resources']['memory'][:-2]) for x in t['containers'])<=8,'memory_boundary')
    for x in t['containers']:
        require(x['image'] in images.values(),'image_drift')
        for v in x.get('env',[]):require(v['name']=='PULSE_SERVICE_TOKEN','unexpected_runtime_credential')
        if x['name'] in ('scanner','signature-updater','laya-model'):require(not x.get('env'),'credential_isolation')
    require(not t.get('initContainers'),'privileged_initialization_not_permitted')
    require(all(x.get('storageName') in (None,STORAGE) for x in t['volumes']),'business_share_mounted')
    require(any(x['name']=='local-socket' and x['storageType']=='EmptyDir' for x in t['volumes']),'nonlocal_socket')
