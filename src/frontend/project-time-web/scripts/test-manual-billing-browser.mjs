import { createRequire } from 'node:module';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { createServer } from 'node:http';
import assert from 'node:assert/strict';
const require = createRequire(import.meta.url);
const tools = process.env.BILLING_BROWSER_TOOLS;
if (!tools) throw new Error('BILLING_BROWSER_TOOLS must point to isolated Playwright/esbuild modules');
const { build } = require(join(tools, 'esbuild'));
const { chromium } = require(join(tools, 'playwright'));
const output = await mkdtemp(join(tmpdir(), 'manual-billing-fixture-'));
await build({ stdin: { contents: `import React,{useState} from 'react';import{createRoot}from'react-dom/client';import ManualInvoicePanel from './src/ManualInvoicePanel.jsx';import BillingReconciliationPanel from './src/BillingReconciliationPanel.jsx';import './src/invoice-billing-enhancements.css';function App(){const[p,setP]=useState('project-a');return <><button onClick={()=>setP(p==='project-a'?'project-b':'project-a')}>Switch project</button><ManualInvoicePanel key={p} projectId={p} projectName={p}/><BillingReconciliationPanel key={p+'invoice'} invoiceId={p+'invoice'}/></>}createRoot(document.getElementById('root')).render(<App/>);`, resolveDir:process.cwd(),loader:'jsx' }, bundle:true,format:'esm',outfile:join(output,'app.js'),jsx:'automatic',plugins:[{name:'fixture-authority-headers',setup(build){build.onResolve({filter:/UnifiedProjectFinancialWorkspace\.jsx$/},()=>({path:'headers',namespace:'test'}));build.onLoad({filter:/.*/,namespace:'test'},()=>({contents:'export const requestHeaders=()=>({});',loader:'js'}));}}] });
await writeFile(join(output,'index.html'),'<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/app.css"><style>*{box-sizing:border-box}body{margin:16px;font:16px system-ui}input,select,textarea{font:inherit}fieldset{min-width:0}dl{margin:0}</style></head><body><div id="root"></div><script type="module" src="/app.js"></script></body></html>');
const server=createServer(async(req,res)=>{try {const file=req.url==='/'?'index.html':req.url.slice(1);if(!['index.html','app.js','app.css'].includes(file)){res.writeHead(404).end();return;}res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html');res.end(await readFile(join(output,file)));}catch{res.writeHead(500).end();}});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const origin=`http://127.0.0.1:${server.address().port}`;
const browser=await chromium.launch({headless:true});let checks=0;
try {
 for(const variant of [{width:1280,height:900,dark:false},{width:390,height:844,dark:true}]){
  const page=await browser.newPage({viewport:{width:variant.width,height:variant.height}});
  const errors=[];page.on('pageerror',error=>errors.push(error.message));const posts=[];
  let fault='abort',pulse=4000,canCreate=true,finalExists=false;
  await page.route('**/api/billing/projects/**',async route=>{
   const req=route.request();const id=req.url().split('/projects/')[1].split('/')[0];
   if(req.method()==='GET'){await route.fulfill({json:{projectId:id,canCreate,canApproveException:true,fixedPrice:true,commercial:{connectorReady:false},basis:{fingerprint:`balance-${pulse}`,pulseInvoiced:pulse,previouslyBilledOutsidePulse:1000,submittedTimeCount:0,pendingTimeCount:0,deliveryComplete:false,closed:false,finalInvoiceExists:finalExists,manualInvoicesExist:pulse>4000,openTransmission:false}}});return;}
   const data=req.postDataJSON();posts.push(data);
   if(fault==='abort'){fault='';await route.abort();return;}
   if(fault==='conflict'){fault='';await route.fulfill({status:409,json:{message:'Balance changed; reload before continuing.'}});return;}
   const amount=data.billToDate-pulse-data.previouslyBilledOutsidePulse;pulse+=amount;finalExists=data.invoiceType==='final';
   await route.fulfill({status:201,json:{status:'billing_invoice_created',invoice:{header:{invoiceNumber:'PULSE-000001-2',totalAmount:amount}}}});
  });
  let recoveryHistory=[],recoveryFault=true;const recoveryPosts=[];
  await page.route('**/api/billing/invoices/**/reconciliation',async route=>{
   if(route.request().method()==='GET'){await route.fulfill({json:{canRecord:canCreate,originalEvidence:{},history:recoveryHistory}});return;}
   const body=route.request().postDataJSON();recoveryPosts.push(body);
   if(recoveryFault){recoveryFault=false;await route.abort();return;}
   recoveryHistory.push({action:'billing_recovery_'+body.action,reference:body.reference,reason:body.reason,recordedAt:new Date().toISOString()});
   await route.fulfill({json:{status:'reconciliation_recorded',message:'Reconciliation recorded without transmission.'}});
  });
  await page.goto(origin);if(variant.dark)await page.locator('html').evaluate(el=>el.setAttribute('data-theme','dark'));
  await page.getByText('Manual partial / full invoice',{exact:true}).click();
  await page.getByText(/No time has been submitted for this project/).waitFor();checks++;
  await page.getByLabel('Billing basis',{exact:true}).selectOption('exception');
  await page.getByLabel('Exception reason',{exact:true}).fill('Finance authorized advance billing despite incomplete time.');
  await page.getByLabel('Progress / milestone / billing instruction reference',{exact:true}).fill('Finance approval for advance billing 123');
  await page.getByLabel('Commercial document and version',{exact:true}).fill('Signed SOW 123 revision 2');
  await page.getByLabel('SELL fallback verification',{exact:true}).fill('Verified signed SOW while SELL unavailable');
  await page.getByLabel('Agreed project total',{exact:true}).fill('10000');
  await page.getByLabel('Cumulative amount to bill through this invoice',{exact:true}).fill('7000');
  await page.getByLabel('Approved SOW / PO / billing authorization',{exact:true}).fill('SOW-123');
  await page.getByLabel('Prior external invoice references',{exact:true}).fill('INV-EXT-01');
  await page.getByLabel('Customer-facing description',{exact:true}).fill('Approved project milestone');
  await page.getByLabel('Internal audit reason',{exact:true}).fill('Approved partial invoice request');
  assert.match(await page.locator('dl').innerText(),/\$2,000\.00/);checks++;
  const contrast = await page.locator('dt').first().evaluate(element => {
    const rgb = value => value.match(/[\d.]+/g).slice(0,3).map(Number).map(v => {v/=255;return v<=0.04045?v/12.92:((v+0.055)/1.055)**2.4;});
    const lum = values => values[0]*0.2126+values[1]*0.7152+values[2]*0.0722;
    const foreground=lum(rgb(getComputedStyle(element).color));
    const background=lum(rgb(getComputedStyle(element.closest('.m042-manual-panel')).backgroundColor));
    return (Math.max(foreground,background)+0.05)/(Math.min(foreground,background)+0.05);
  });
  assert.ok(contrast>=4.5,`Billing balance labels contrast: ${contrast}`);checks++;
  await page.locator('details').filter({has:page.getByText('Manual partial / full invoice',{exact:true})}).getByRole('checkbox').check();await page.getByRole('button',{name:'Create partial invoice',exact:true}).click();
  await page.getByRole('button',{name:'Retry same invoice request',exact:true}).waitFor();
  await page.getByRole('button',{name:'Retry same invoice request',exact:true}).click();
  await page.getByText(/PULSE-000001-2 saved/).waitFor();assert.deepEqual(posts[0],posts[1]);checks++;
  await page.getByRole('combobox',{name:'Invoice type',exact:true}).selectOption('final');
  await page.getByLabel('Customer-facing description',{exact:true}).fill('Final authorized project balance');
  await page.getByLabel('Internal audit reason',{exact:true}).fill('Reconciled final project billing');
  assert.match(await page.locator('dl').innerText(),/\$3,000\.00/);checks++;
  fault='conflict';await page.locator('details').filter({has:page.getByText('Manual partial / full invoice',{exact:true})}).getByRole('checkbox').check();await page.getByRole('button',{name:'Create full / final invoice',exact:true}).click();
  await page.getByText('Balance changed; reload before continuing.',{exact:true}).waitFor();
  await page.waitForFunction(()=>!document.querySelector('.m042-manual-confirm input')?.checked);checks++;
  await page.locator('details').filter({has:page.getByText('Manual partial / full invoice',{exact:true})}).getByRole('checkbox').check();await page.getByRole('button',{name:'Create full / final invoice',exact:true}).click();
  await page.getByText('A final invoice is already recorded. Review the invoice history below.',{exact:true}).waitFor();checks++;
  assert.equal(await page.getByRole('button',{name:'Create full / final invoice',exact:true}).count(),0);
  await page.getByRole('button',{name:'Switch project',exact:true}).click();
  await page.getByText('Manual partial / full invoice',{exact:true}).click();
  await page.getByRole('heading',{name:'Manual invoice · project-b',exact:true}).waitFor();checks++;
  const recoveryPanel=page.locator('details').filter({has:page.getByText('Billing reconciliation and offline tracking',{exact:true})});
  await recoveryPanel.locator('summary').click();
  await recoveryPanel.getByLabel('Reconciliation action',{exact:true}).selectOption('manual_handoff');
  await recoveryPanel.getByLabel('Verified reference',{exact:true}).fill('MANUAL-001');
  await recoveryPanel.getByLabel('Reconciliation reason',{exact:true}).fill('Billing verified delivery outside the unavailable connector.');
  await recoveryPanel.getByRole('checkbox').check();
  await recoveryPanel.getByRole('button',{name:'Record reconciliation',exact:true}).click();
  await recoveryPanel.getByRole('button',{name:'Retry same reconciliation',exact:true}).click();
  await recoveryPanel.getByText('Reconciliation recorded without transmission.',{exact:true}).waitFor();
  assert.deepEqual(recoveryPosts[0],recoveryPosts[1]);checks++;
  await recoveryPanel.getByText(/manual handoff · MANUAL-001/).waitFor();checks++;
  canCreate=false;finalExists=false;await page.getByRole('button',{name:'Reload billing balance',exact:true}).click();
  await page.getByText('Your current role can view billing but cannot create invoices.',{exact:true}).waitFor();checks++;
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1),false);assert.deepEqual(errors,[]);checks++;
  await page.close();
 }
 console.log(`MANUAL_BILLING_BROWSER=PASS checks=${checks} desktop-light/mobile-dark`);
} finally { await browser.close();await new Promise(resolve=>server.close(resolve));await rm(output,{recursive:true,force:true}); }
