"""Synthetic private-service acceptance; outputs no documents or credentials."""
import hashlib,http.client,io,ipaddress,json,os,socket,ssl,time,uuid
from datetime import datetime,timezone
from urllib.parse import urlsplit
from test_resources import DOMAIN,APPS
from laya_protocol import normalized
from activation_contracts import validate_laya_health
class PinnedHTTPS(http.client.HTTPSConnection):
    def connect(self):
        addresses=socket.getaddrinfo(self.host,443,type=socket.SOCK_STREAM)
        if not addresses:raise ValueError('private_dns_empty')
        safe=[]
        for row in addresses:
            ip=ipaddress.ip_address(row[4][0])
            if ip.is_loopback or ip.is_link_local or not any(ip in ipaddress.ip_network(n) for n in ('10.0.0.0/8','172.16.0.0/12','192.168.0.0/16') if ip.version==4):
                raise ValueError('private_dns_rejected')
            safe.append(row[4][0])
        self.sock=ssl.create_default_context().wrap_socket(socket.create_connection((safe[0],443),self.timeout),server_hostname=self.host)
def call(kind,path,method='GET',data=None,content_type='application/json',authorized=True):
    host=APPS[kind]+'.internal.'+DOMAIN
    if path not in ('/health','/health/live','/v1/scan','/v1/extract','/v1/decisions/health','/v1/decisions/document-type'):raise ValueError('path_rejected')
    headers={'Content-Type':content_type,'X-Pulse-AI-Privacy-Boundary':'private_pulse_runtime_only','Cache-Control':'no-store'}
    if authorized:headers['Authorization']='Bearer '+os.environ['PULSE_'+kind.upper()+'_TOKEN']
    c=PinnedHTTPS(host,timeout=240)
    try:
        c.request(method,path,body=data,headers=headers);resp=c.getresponse();raw=resp.read(6_100_001)
        if len(raw)>6_100_000:raise ValueError('response_too_large')
        return resp.status,json.loads(raw)
    finally:c.close()
def upload(content,ocr=False):
    boundary='pulse-'+uuid.uuid4().hex
    crlf=bytes((13,10))
    header=(f'--{boundary}', 'Content-Disposition: form-data; name="file"; filename="synthetic.input"', 'Content-Type: application/octet-stream', '')
    chunks=[crlf.join(x.encode() for x in header)+crlf, content, crlf]
    if ocr:
        for key,value in {'documentId':str(uuid.uuid4()),'documentCategory':'validation','model':'tesseract-5-eng'}.items():
            chunks.append(crlf.join(x.encode() for x in [f'--{boundary}',f'Content-Disposition: form-data; name="{key}"','',value,'']))
    chunks.append(f'--{boundary}--'.encode()+crlf)
    return call('documents','/v1/extract' if ocr else '/v1/scan','POST',b''.join(chunks),'multipart/form-data; boundary='+boundary)
def main():
    from PIL import Image,ImageDraw,ImageFont
    for kind,path in [('documents','/health'),('laya','/v1/decisions/health')]:
        status,_=call(kind,path,authorized=False);assert status==401,'unauthorized_request_accepted'
        status,body=call(kind,path);assert status==200,'service_not_ready'
        if kind=='laya':validate_laya_health(body)
        else:
            assert body['modelProviderRequired'] is False and body['scanner']=='clamav'
            age=(datetime.now(timezone.utc)-datetime.fromisoformat(body['engine']['updated_at'])).total_seconds()
            assert -300<=age<=48*3600,'signature_freshness'
    content=b'PULSE CONTROLLED CLEAN ACCEPTANCE FIXTURE\n'
    status,body=upload(content);assert status==200 and body['clean'] is True and body['infected'] is False
    assert body['sha256']==hashlib.sha256(content).hexdigest() and body['sizeBytes']==len(content)
    # Harmless industry-standard antivirus test bytes, not a malware sample.
    test=bytes.fromhex('58354f2150254041505b345c505a58353428505e2937434329377d2445494341522d5354414e444152442d414e544956495255532d544553542d46494c452124482b482a')
    status,body=upload(test);assert status==200 and body['infected'] is True and body['clean'] is False,'antivirus_test_detection'
    image=Image.new('RGB',(1000,250),'white');draw=ImageDraw.Draw(image)
    font=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',42);draw.text((25,80),'PULSE LOCAL OCR VERIFIED',font=font,fill='black')
    for format in ('PNG','PDF'):
        buffer=io.BytesIO();image.save(buffer,format=format)
        status,body=upload(buffer.getvalue(),True);assert status==200,'ocr_failed'
        assert 'PULSE LOCAL OCR VERIFIED' in ' '.join(p['text'] for p in body['pages']),'ocr_text_missing'
        assert body['scan']['clean'] is True and body['scan']['sha256']==body['sha256'],'ocr_scan_mismatch'
    status,_=upload(b'/private/file.png\n',True);assert status in (400,413,415,422),'unsafe_ocr_input_accepted'
    for text in ['STATEMENT OF WORK. Example deliverables and acceptance criteria.','INVOICE. Example services total USD 100.','PURCHASE ORDER. Example equipment purchase.','Ordinary synthetic meeting notes.']:
        status,body=call('laya','/v1/decisions/document-type','POST',json.dumps({'text':text}).encode())
        assert status==200;normalized(body)
    print('PULSE_PRIVATE_SERVICE_ACCEPTANCE='+json.dumps({'passed':True,'cleanScan':True,'antivirusTestDetected':True,
        'pngOcr':True,'pdfOcr':True,'unsafeImageRejected':True,'realLayaCases':4,'unauthorizedRejected':True,
        'sourceSha':os.environ['PULSE_EXPECTED_SOURCE'],'rawDocumentContentPublished':False,'productionMutation':False}))
if __name__=='__main__':
    try:main()
    except Exception as e:
        print('PULSE_PRIVATE_SERVICE_ACCEPTANCE='+json.dumps({'passed':False,'diagnostic':type(e).__name__,'rawDocumentContentPublished':False}));raise SystemExit(1)
