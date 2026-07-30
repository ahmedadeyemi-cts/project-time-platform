#!/usr/bin/env python3
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import hashlib, os, shutil, subprocess
from docx import Document
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Pt, RGBColor
from PIL import Image, ImageDraw, ImageFont
from reportlab.pdfgen import canvas
from reportlab.lib.pagesizes import A3, landscape
from reportlab.lib import colors
from reportlab.lib.units import inch
from pypdf import PdfReader
import cairosvg

S=Path(__file__).resolve().parent; O=S.parent; V=O.parent/'v1.1'; O.mkdir(exist_ok=True)
LOGO=V/'source/US_Signal_Logo.jpg'; DATE='July 30, 2026'; ISO='2026-07-30'
DOC=V/'US_Signal_Pulse_AI_Private_Intelligence_Architecture_v1.1.docx'
LS=V/'US_Signal_Pulse_AI_Logical_Architecture_v1.1.svg'; DS=V/'US_Signal_Pulse_AI_Deployment_Network_Architecture_v1.1.svg'
N={
'docx':'US_Signal_Celar_AI_Private_Intelligence_Architecture_v2.0.docx',
'pdf':'US_Signal_Celar_AI_Private_Intelligence_Architecture_v2.0.pdf',
'dpdf':'US_Signal_Celar_AI_Architecture_Diagrams_v2.0.pdf',
'lpng':'US_Signal_Celar_AI_Logical_Architecture_v2.0.png','lsvg':'US_Signal_Celar_AI_Logical_Architecture_v2.0.svg',
'dpng':'US_Signal_Celar_AI_Deployment_Network_Architecture_v2.0.png','dsvg':'US_Signal_Celar_AI_Deployment_Network_Architecture_v2.0.svg'}
CANON=('Celar AI is the unified operational intelligence system for the US Signal Solution Provider division. It was conceived and engineered under the direction of Dr. Ahmed Adeyemi, Manager of Professional Services, to create a central intersection where consulting teams can convene, collaborate, and exchange project, delivery, operational, and financial information. The name draws from celeritas, the Latin concept of swiftness or speed, and from the conventional symbol c for the speed of light in E=mc². That connection reflects US Signal\'s fiber-network heritage and Celar AI\'s mission: translate the speed of light into the speed of delivery. From a solution-provider perspective, Celar AI reduces the operational drag associated with legacy PSA workflows, including siloed information, repetitive administration, slow SOW creation, fragmented task handoffs, time-entry friction, and delayed financial visibility. It unifies authorized documents, live system data, workflows, and AI-assisted reasoning so teams can scope, execute, troubleshoot, report, and invoice work more quickly without abandoning security, governance, or human accountability.')

def font(n,b=False):
 p='/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf'%('-Bold' if b else '')
 return ImageFont.truetype(p,n) if Path(p).exists() else ImageFont.load_default()
def hashf(p):
 h=hashlib.sha256();
 with open(p,'rb') as f:
  for b in iter(lambda:f.read(1048576),b''): h.update(b)
 return h.hexdigest()
def svg(src,out,logical):
 t=src.read_text();
 for a,b in [('PULSE AI ARCHITECTURE','CELAR AI ARCHITECTURE'),('Pulse AI Architecture | Version 1.1','Celar AI Architecture | Version 2.0'),('Private Pulse AI Model Endpoint','Private Celar AI Model Endpoint'),('Private Pulse AI Model','Private Celar AI Model'),('PRIVATE PULSE AI TRUST ZONE','PRIVATE CELAR AI TRUST ZONE')]: t=t.replace(a,b)
 t=t.replace('PRIVATE-FIRST LOGICAL ARCHITECTURE','PRIVATE-FIRST LOGICAL ARCHITECTURE | SPEED OF DELIVERY') if logical else t.replace('DEPLOYMENT AND NETWORK ARCHITECTURE','DEPLOYMENT AND NETWORK ARCHITECTURE | CELAR AI')
 out.write_text(t)
def banner(p):
 im=Image.new('RGB',(1800,360),'white'); d=ImageDraw.Draw(im); lg=Image.open(LOGO).convert('RGB'); lg.thumbnail((280,280)); im.paste(lg,(45,35)); d.text((350,85),'CELAR AI ARCHITECTURE',font=font(84,1),fill='#072D59'); d.text((357,210),'PRIVATE-FIRST INTELLIGENCE PLATFORM',font=font(34),fill='#5F6B7A'); d.rounded_rectangle((355,275,1710,296),11,fill='#1737FF'); im.save(p)
def connected(p):
 im=Image.new('RGB',(1800,1000),'#F8FAFD'); d=ImageDraw.Draw(im); d.text((90,95),'CONNECTED. PRIVATE. GOVERNED.',font=font(58,1),fill='#072D59'); d.text((94,180),'Celar AI architecture for secure reasoning across documents, data and approved model services.',font=font(28),fill='#5F6B7A'); d.text((94,235),'Celeritas: speed of light. Celar AI: speed of delivery.',font=font(26,1),fill='#1769AA'); pts=[(170,620),(540,470),(910,660),(1280,410),(1570,300)]; cs=['#1737FF','#1737FF','#072D59','#072D59','#1737FF'];
 for i in range(4): d.line([pts[i],pts[i+1]],fill='#5E92C9',width=5)
 for (x,y),c in zip(pts,cs):
  d.ellipse((x-24,y-24,x+24,y+24),fill=c)
  for dx,dy in [(0,-110),(0,110),(-100,0),(100,0),(-75,-75),(75,-75),(-75,75),(75,75)]: ex,ey=x+dx,y+dy; d.line((x,y,ex,ey),fill=c,width=4); d.ellipse((ex-10,ey-10,ex+10,ey+10),fill=c)
 d.rectangle((1640,0,1800,1000),fill='#DCE5EF'); d.rectangle((1600,0,1645,1000),fill='#1737FF'); im.save(p)
def paras(doc):
 yield from doc.paragraphs
 for t in doc.tables:
  for r in t.rows:
   for c in r.cells: yield from c.paragraphs
 for s in doc.sections: yield from s.header.paragraphs; yield from s.footer.paragraphs
def ins(doc,target,text,style=None):
 p=doc.add_paragraph(text,style); target._p.addprevious(p._p); return p
def shade(c,fill):
 pr=c._tc.get_or_add_tcPr(); x=pr.find(qn('w:shd')) or OxmlElement('w:shd'); x.set(qn('w:fill'),fill); pr.append(x) if x.getparent() is None else None

def make_doc(lg,dp,bn,cn,out):
 d=Document(DOC)
 for p in paras(d):
  for r in p.runs: r.text=r.text.replace('Pulse AI','Celar AI').replace('PULSE AI','CELAR AI')
 for p in d.paragraphs:
  if p.text.strip()=='Private-first document intelligence, governed live-data reasoning, and controlled external LLM escalation': p.text='Speed of light. Speed of delivery. Private-first intelligence for unified solution-provider operations.'; p.alignment=WD_ALIGN_PARAGRAPH.CENTER
  if p.text.startswith('Pulse is the business platform.') or p.text.startswith('Celar AI is the business platform.'):
   p.text='Pulse is the business platform. Celar AI is the target brand for the private intelligence capability in Module 011. Module 064 remains the governed external-provider gateway. The existing Pulse AI runtime name and technical identifiers remain unchanged until a separate rebrand implementation is approved.'
 t=d.tables[0]; vals={'DOCUMENT ID':'USSI-CELAR-AI-ARCH-001','VERSION':'2.0 - Celar AI Brand and Origin Revision','DATE':DATE,'PREPARED BY':'US Signal','PLATFORM NAME':'Pulse / Celar AI','CLASSIFICATION':'US Signal Internal - Confidential'}
 for r in t.rows:
  if r.cells[0].text.strip() in vals: r.cells[1].text=vals[r.cells[0].text.strip()]
 rev=d.tables[3].add_row(); [setattr(rev.cells[i],'text',x) for i,x in enumerate(['2.0',DATE,'US Signal','Rebranded Module 011 architecture from Pulse AI to Celar AI; added creator attribution, Celeritas origin, fiber alignment, Changepoint catalyst, speed-of-delivery mission, canonical answer, and brand-governance note.'])]
 target=next(p for p in d.paragraphs if p.text.strip()=='Primary business capabilities')
 blocks=[('1.1 Celar AI Identity and Origin','Heading 2'),('Celar AI is the unified operational intelligence system for the US Signal Solution Provider division. It was conceived and engineered under the direction of Dr. Ahmed Adeyemi, Manager of Professional Services, to serve as the central intersection where consulting teams convene, collaborate, and exchange critical project information.',None),('The meaning behind the name','Heading 3'),('The name Celar AI draws from celeritas, a Latin term associated with swiftness and speed, and from the conventional symbol c for the speed of light in E=mc². The connection pays homage to US Signal\'s fiber-optic network foundation while translating speed from a network property into an operating principle for professional services.',None),('For Celar AI, the brand promise is simple: speed of light becomes speed of delivery. The system reduces the time required to scope, plan, execute, troubleshoot, report, and invoice customer solutions.',None),('The catalyst: overcoming the Changepoint struggle','Heading 3'),('Changepoint served as a functional legacy professional-services automation system and system of record, but rigid navigation, siloed information, repetitive administration, and manual handoffs created operational drag. Consultants spent valuable technical time on administrative entry, and SOW, task, time, financial, and invoice workflows progressed more slowly than the delivery organization required.',None),('Celar AI addresses that friction without abandoning governance. It brings authorized documents, project data, time, tasks, financial context, system health, troubleshooting evidence, and AI-assisted reasoning into one governed operational intelligence layer.',None),('Solution-provider mission','Heading 3'),('Celar AI is the unified intersection where Sales, Professional Services, Project Management, Engineering, Finance, Operations, Security, and leadership exchange information. It accelerates delivery while preserving source-system ownership, least-privilege access, deterministic calculations, human review, and audit evidence.',None)]
 for text,style in blocks: ins(d,target,text,style)
 tb=d.add_table(rows=1,cols=1); tb.style='Table Grid'; c=tb.cell(0,0); c.text=''; shade(c,'EAF5FB'); p=c.paragraphs[0]; rr=p.add_run('Canonical answer: “What is Celar AI?”'); rr.bold=True; rr.font.color.rgb=RGBColor(7,45,89); c.add_paragraph(CANON); target._p.addprevious(tb._tbl)
 ins(d,target,'Brand governance note','Heading 3'); ins(d,target,'Celar AI is a strong internal working brand, but Celar is already used by other organizations. Before public marketing, trademark filing, domain acquisition, or customer-facing launch, US Signal Legal and Marketing should complete name-clearance, trademark, pronunciation, and digital-identity review.',None)
 d.add_page_break(); d.add_heading('Appendix D. Naming and Brand References',1)
 for x in ['PBS NOVA notes celeritas as Latin for swiftness and c as the speed of light.','US Signal official materials describe the company as a digital infrastructure and solution provider powered by fiber assets.','Third parties use Celar in technology and payments; external launch requires legal and trademark review.']: d.add_paragraph(x,style='List Bullet')
 d.core_properties.title='US Signal Celar AI Private Intelligence Architecture'; d.core_properties.author='US Signal'; d.core_properties.comments='Version 2.0 documentation-only rebrand baseline; runtime names unchanged.'
 tmp=O/'_t.docx'; d.save(tmp)
 with ZipFile(tmp) as z, ZipFile(out,'w',ZIP_DEFLATED) as w:
  for i in z.infolist():
   b=z.read(i.filename)
   if i.filename=='word/media/image1.png': b=bn.read_bytes()
   elif i.filename=='word/media/image2.png': b=cn.read_bytes()
   elif i.filename=='word/media/image3.png': b=lg.read_bytes()
   elif i.filename=='word/media/image4.png': b=dp.read_bytes()
   if i.filename.endswith('.xml'):
    x=b.decode('utf-8').replace('USSI-PULSE-AI-ARCH-001','USSI-CELAR-AI-ARCH-001').replace('Version 1.1','Version 2.0').replace('Pulse AI','Celar AI').replace('PULSE AI','CELAR AI')
    x=x.replace('Initial private-first Celar AI architecture baseline.','Initial private-first Pulse AI architecture baseline.').replace('Rebranded Module 011 architecture from Celar AI to Celar AI;','Rebranded Module 011 architecture from Pulse AI to Celar AI;').replace('The existing Celar AI runtime name','The existing Pulse AI runtime name'); b=x.encode()
   w.writestr(i,b)
 tmp.unlink()
def pdf(docx,name):
 t=O/'_lo'; shutil.rmtree(t,ignore_errors=True); t.mkdir(); env=os.environ|{'HOME':str(t/'h')}; Path(env['HOME']).mkdir(); subprocess.run(['libreoffice','--headless','--nologo','--nofirststartwizard','-env:UserInstallation=file://'+str(t/'p'),'--convert-to','pdf','--outdir',str(t),str(docx)],check=True,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE); shutil.copy2(t/(docx.stem+'.pdf'),O/name); shutil.rmtree(t)
def diagpdf(lp,dp,out):
 pg=landscape(A3); c=canvas.Canvas(str(out),pagesize=pg)
 for title,p in [('Celar AI Private-First Logical Architecture',lp),('Celar AI Deployment and Network Architecture',dp)]:
  c.setFillColor(colors.white); c.rect(0,0,*pg,fill=1,stroke=0); c.setFillColor(colors.HexColor('#072D59')); c.setFont('Helvetica-Bold',18); c.drawString(.45*inch,pg[1]-.42*inch,title); im=Image.open(p); sc=min((pg[0]-.7*inch)/im.width,(pg[1]-.9*inch)/im.height); w,h=im.width*sc,im.height*sc; c.drawImage(str(p),(pg[0]-w)/2,(pg[1]-h)/2-.08*inch,w,h,mask='auto'); c.setFont('Helvetica',7); c.drawString(.45*inch,.25*inch,'US Signal Internal - Confidential'); c.showPage()
 c.save()
def texts():
 ident=f'''# Celar AI Identity and Origin\n\n**Owner:** US Signal  \n**Platform:** Pulse  \n**Module:** 011  \n**Target brand:** Celar AI  \n**Status:** Documentation-first rebrand baseline  \n**Effective date:** {ISO}\n\n## Core identity\n\nCelar AI is the unified operational intelligence system for the US Signal Solution Provider division. Conceived and engineered under the direction of **Dr. Ahmed Adeyemi, Manager of Professional Services**, it is the central intersection where consulting teams convene, collaborate, and exchange critical project information.\n\n## Meaning behind the name\n\nThe name draws from **Celeritas**, the Latin concept of swiftness or speed, and from the conventional symbol **c** for the speed of light in `E=mc²`. It connects US Signal's fiber-network foundation to the Professional Services mission of turning speed of light into **speed of delivery**.\n\n## Catalyst: overcoming the Changepoint struggle\n\nChangepoint served as a functional legacy PSA and system of record, but rigid navigation, siloed information, repetitive administration, and manual handoffs created operational drag. Celar AI unifies authorized documents, live system data, workflows, troubleshooting evidence, time, tasks, reports, and financial context so teams can scope, execute, and invoice solutions faster.\n\n## Canonical response\n\n> {CANON}\n\n## Brand governance\n\nThis package establishes the target internal brand. Runtime and source identifiers remain Pulse AI until a separate implementation is approved. Because third parties already use Celar, public launch requires formal US Signal Legal and Marketing clearance.\n'''; (O/'CELAR-AI-IDENTITY-AND-ORIGIN.md').write_text(ident)

def main():
 lsvg=O/N['lsvg']; dsvg=O/N['dsvg']; svg(LS,lsvg,1); svg(DS,dsvg,0); lp=O/N['lpng']; dp=O/N['dpng']; cairosvg.svg2png(bytestring=lsvg.read_bytes(),write_to=str(lp),output_width=3200); cairosvg.svg2png(bytestring=dsvg.read_bytes(),write_to=str(dp),output_width=4200)
 ldoc=O/'_l.png'; ddoc=O/'_d.png'; cairosvg.svg2png(bytestring=lsvg.read_bytes(),write_to=str(ldoc),output_width=3881,output_height=2573); cairosvg.svg2png(bytestring=dsvg.read_bytes(),write_to=str(ddoc),output_width=6444,output_height=1879); bn=O/'_b.png'; cn=O/'_c.png'; banner(bn); connected(cn); make_doc(ldoc,ddoc,bn,cn,O/N['docx']); pdf(O/N['docx'],N['pdf']); diagpdf(lp,dp,O/N['dpdf']); texts()
 tracked=[O/x for x in N.values()]+[O/'CELAR-AI-IDENTITY-AND-ORIGIN.md',Path(__file__)]; hs={str(p.relative_to(O)):hashf(p) for p in tracked}
 r='# US Signal Celar AI Architecture Package - Version 2.0\n\n**Owner:** US Signal  \n**Platform:** Pulse  \n**Module:** 011 - Celar AI target brand  \n**Classification:** US Signal Internal - Confidential  \n**Status:** Documentation-first rebrand baseline  \n**Published:** '+ISO+'\n\nThis package rebrands the Module 011 architecture from Pulse AI to Celar AI before any runtime rename. It includes the Celeritas origin, Dr. Ahmed Adeyemi attribution, US Signal fiber alignment, Changepoint catalyst, speed-of-delivery mission, private-first architecture, and canonical Celar AI response.\n\n## Files\n\n| File | SHA-256 |\n|---|---|\n'+''.join(f'| `{n}` | `{h}` |\n' for n,h in sorted(hs.items()))+'\n## Transition boundary\n\nDocumentation and diagrams only. Runtime routes, APIs, database objects, feature codes, environment variables, permissions, source folders, Module 064 configuration, and deployments remain unchanged.\n\n## Brand review\n\nCelar AI is aligned to US Signal fiber and delivery identity, but Celar is not globally unique. Public use requires legal, trademark, domain, pronunciation, and visual-identity review.\n'; (O/'README.md').write_text(r); hs['README.md']=hashf(O/'README.md'); (O/'SHA256SUMS.txt').write_text(''.join(f'{h}  {n}\n' for n,h in sorted(hs.items())))
 for p in [ldoc,ddoc,bn,cn]: p.unlink(missing_ok=True)
 assert len(PdfReader(str(O/N['pdf'])).pages)>=30
 print('CELAR_AI_ARCHITECTURE_V2_GENERATION=PASSED')
if __name__=='__main__': main()
