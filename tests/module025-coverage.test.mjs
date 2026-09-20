import assert from 'node:assert/strict';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const web = path.join(root, 'src/frontend/project-time-web');
const require = createRequire(path.join(web, 'package.json'));
const { createServer } = await import(path.join(web, 'node_modules/vite/dist/node/index.js'));
const { default: react } = await import(path.join(web, 'node_modules/@vitejs/plugin-react/dist/index.js'));
const { chromium } = require(process.env.PLAYWRIGHT_MODULE_PATH || 'playwright');
const harness = `<div id="root"></div><script type="module">import React,{useState}from'react';import{createRoot}from'react-dom/client';import Transfer from'/src/module025/OwnershipTransfer.jsx';
async function request(url,options){const r=await fetch(url,options);const data=await r.json();if(!r.ok){const error=new Error(data.message);error.status=r.status;throw error;}return data;}
function Harness(){const[epoch,setEpoch]=useState(0),[result,setResult]=useState(''),[busy,setBusy]=useState(false),[disabled,setDisabled]=useState(false);
return React.createElement(React.Fragment,null,React.createElement('output',{id:'parent-state'},JSON.stringify({result,busy})),React.createElement('button',{onClick:()=>setEpoch(x=>x+1)},'Reload handoff'),React.createElement('button',{onClick:()=>{setDisabled(true);setEpoch(x=>x+1)}},'Switch to read-only'),React.createElement(Transfer,{key:epoch,engagement:{engagementId:'coverage-fixture',revision:epoch+7},request,disabled,notificationsEnabled:true,onBusyChanged:setBusy,onTransferred:r=>{setResult(JSON.stringify(r));setEpoch(x=>x+1)}}));}createRoot(document.getElementById('root')).render(React.createElement(Harness));</script>`;
const server = await createServer({ configFile: false, root: web, cacheDir: path.join(web, 'node_modules/.vite-module025-coverage-harness'), plugins: [react(), { name: 'coverage-harness', configureServer(vite) { vite.middlewares.use('/__coverage', async (req,res) => { res.setHeader('Content-Type','text/html');res.end(await vite.transformIndexHtml('/__coverage',harness)); }); } }], server:{host:'127.0.0.1',port:0} });
await server.listen();
const browser = await chromium.launch({headless:true,...(process.env.MODULE025_TEST_CHROMIUM ? {executablePath:process.env.MODULE025_TEST_CHROMIUM,args:['--no-sandbox','--disable-dev-shm-usage']} : {})});
try {
  const context=await browser.newContext({locale:'en-US',timezoneId:'Pacific/Honolulu'});
  const page=await context.newPage();await page.clock.install({time:new Date('2026-09-20T00:30:00Z')});
  const errors=[],writes=[];page.on('pageerror',error=>errors.push(error.message));
  let mode='fresh',revision=7,owner='Alex',inactiveOriginal=false,readOnly=false;
  const handoff={handoffId:'handoff-1',mode:'temporary',previousOwnerDisplayName:'Alex System',newOwnerDisplayName:'Bea System',returnDate:'2026-09-23',acknowledgedAt:null,returnedAt:null};
  await page.route('**/api/**',async route=>{
    const req=route.request(),url=new URL(req.url());let body;
    if(req.method()==='POST'){
      const payload=req.postDataJSON();writes.push({path:url.pathname,payload});assert.equal(readOnly,false);
      if(url.pathname.endsWith('/transfer')){
        assert.deepEqual(payload,{targetOwnerUserId:'bea',expectedRevision:7,reason:'PTO coverage: finish the estimate review.',mode:'temporary',returnDate:'2026-09-23'});
        mode='covering';owner='Bea';revision=8;body={engagementId:'coverage-fixture',ownerDisplayName:'Bea System',revision};
      }else if(url.pathname.endsWith('/handoff/acknowledge')){
        assert.deepEqual(payload,{handoffId:'handoff-1',expectedRevision:8});handoff.acknowledgedAt='2026-09-20T00:30:00Z';body={acknowledged:true};
      }else if(url.pathname.endsWith('/handoff/return')){
        assert.deepEqual(payload,{handoffId:'handoff-1',expectedRevision:8,reason:'Estimate reviewed; customer approval remains.'});assert.equal(inactiveOriginal,false);
        mode='returned';owner='Alex';revision=9;handoff.returnedAt='2026-09-20T01:00:00Z';body={engagementId:'coverage-fixture',ownerDisplayName:'Alex System',revision};
      }else throw new Error('Unexpected mutation '+url.pathname);
    }else if(url.pathname.endsWith('/transfer-options')){
      body={revision,coverageReady:true,canTransfer:!readOnly&&mode==='fresh',destinations:[{userId:'bea',displayName:'Bea System',teamName:'System SA'}],
        blockedReason:readOnly?'View-As is read-only.':mode==='covering'?'Return current coverage before another transfer.':null,
        activeCoverage:mode==='covering'?handoff:null,latestHandoff:mode==='fresh'?null:handoff,
        canAcknowledge:!readOnly&&mode==='covering'&&!handoff.acknowledgedAt,canReturn:!readOnly&&mode==='covering'&&!inactiveOriginal,
        returnBlockedReason:inactiveOriginal?'The original owner is inactive. An authorized manager must arrange another owner.':readOnly?'View-As is read-only.':null};
    }else if(url.pathname.endsWith('/handoff-notifications')){
      body={message:'Events are queued through Module 065. Delivery is not confirmed.',items:[{eventId:'event-1',kind:'ownership_handoff',status:'queued',deliveryBoundary:'test_only',occurredAt:'2026-09-20T00:30:00Z'}]};
    }else throw new Error('Unexpected request '+url.pathname);
    await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(body)});
  });
  await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__coverage`);
  const widget=page.locator('.m025-transfer');await widget.locator('summary').click();
  await widget.getByLabel(/^New responsible Solution Architect/).selectOption('bea');
  await widget.getByLabel(/^Handoff type/).selectOption('temporary');
  const date=widget.getByLabel(/^Expected return date \(UTC\)/);
  assert.equal(await date.getAttribute('min'),'2026-09-20','coverage reminder uses documented UTC boundary');
  await widget.getByLabel('Handoff reason and context',{exact:true}).fill(' PTO coverage: finish the estimate review. ');
  const start=widget.getByRole('button',{name:'Start temporary coverage',exact:true});
  await date.fill('2026-09-19');assert.equal(await start.isDisabled(),true,'past UTC return date blocked even when local day is earlier');
  await date.fill('2026-09-23');await start.click();
  await widget.locator('summary').click();
  await widget.getByText('Temporary coverage is active',{exact:true}).waitFor();
  assert.equal(owner,'Bea');assert.equal(revision,8);
  assert.ok((await widget.getByRole('region',{name:'Current handoff'}).innerText()).includes('Sep 23, 2026'),'calendar date does not shift west of UTC');
  const delivery=widget.getByRole('region',{name:'Handoff notification delivery'});
  await delivery.getByText('queued',{exact:true}).waitFor();
  assert.ok((await delivery.innerText()).includes('Test delivery boundary'));
  assert.equal(/\bsent\b|\bdelivered\b/i.test(await delivery.innerText()),false,'queued/test-only is not represented as delivery');
  const transferred=JSON.parse(await page.locator('#parent-state').textContent()).result;
  await widget.getByRole('button',{name:'Acknowledge handoff',exact:true}).click();
  await widget.getByText('Handoff acknowledged. Your acknowledgement is recorded in the history.',{exact:true}).waitFor();
  assert.equal(owner,'Bea','acknowledgement does not transfer ownership');assert.equal(revision,8);
  assert.equal(JSON.parse(await page.locator('#parent-state').textContent()).result,transferred,'acknowledgement does not invoke transfer callback');
  assert.equal(await widget.getByRole('button',{name:'Acknowledge handoff',exact:true}).count(),0);
  inactiveOriginal=true;await page.getByRole('button',{name:'Reload handoff',exact:true}).click();await widget.locator('summary').click();
  await widget.getByText('The original owner is inactive. An authorized manager must arrange another owner.',{exact:true}).waitFor();
  assert.equal(await widget.getByRole('button',{name:'Return ownership to Alex System',exact:true}).count(),0);
  inactiveOriginal=false;await page.getByRole('button',{name:'Reload handoff',exact:true}).click();await widget.locator('summary').click();
  const returnButton=widget.getByRole('button',{name:'Return ownership to Alex System',exact:true});await returnButton.waitFor();
  assert.equal(await returnButton.isDisabled(),true,'return requires context');
  await widget.getByLabel('Return handoff notes',{exact:true}).fill(' Estimate reviewed; customer approval remains. ');await returnButton.click();
  await widget.locator('summary').click();await widget.getByText('Temporary coverage completed',{exact:true}).waitFor();
  assert.equal(owner,'Alex');assert.equal(revision,9);assert.equal(writes.length,3);
  readOnly=true;await page.getByRole('button',{name:'Switch to read-only',exact:true}).click();await widget.locator('summary').click();
  await widget.getByText('View-As is read-only.',{exact:true}).waitFor();
  for(const action of ['Start temporary coverage','Transfer this SOW / GSD','Acknowledge handoff','Return ownership to Alex System'])assert.equal(await widget.getByRole('button',{name:action,exact:true}).count(),0);
  assert.equal(writes.length,3);assert.deepEqual(errors,[]);
  console.log('MODULE025_COVERAGE=PASS temporaryPayload=UTC acknowledgement=nonTransfer return=explicit inactiveOwner=blocked notificationStatus=truthful readonlyWrites=0');
  await context.close();
}finally{await browser.close();await server.close();}
