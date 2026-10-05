import { createRequire } from 'node:module';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createServer } from 'node:http';
const require = createRequire(import.meta.url);
const tools = process.env.BILLING_BROWSER_TOOLS;
if (!tools) throw new Error('BILLING_BROWSER_TOOLS missing');
const { build } = require(join(tools, 'esbuild'));
const { chromium } = require(join(tools, 'playwright'));
const output = await mkdtemp(join(tmpdir(), 'invoice-empty-render-'));
await build({
  stdin: {
    contents: `import React from 'react';import{createRoot}from'react-dom/client';import InvoiceBillingCenter from './src/InvoiceBillingCenter.jsx';createRoot(document.getElementById('root')).render(<InvoiceBillingCenter usSignalLogoUrl="" userKey="demo"/>);`,
    resolveDir: process.cwd(),
    loader: 'jsx'
  },
  bundle: true,
  format: 'esm',
  outfile: join(output, 'app.js'),
  jsx: 'automatic'
});
await writeFile(join(output,'index.html'),'<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"></head><body><div id="root"></div><script type="module" src="/app.js"></script></body></html>');
const server=createServer(async(req,res)=>{try{const f=req.url==='/'?'index.html':req.url.slice(1);if(!['index.html','app.js'].includes(f)){res.writeHead(404).end();return;}res.setHeader('Content-Type',f.endsWith('.js')?'text/javascript':'text/html');res.end(await readFile(join(output,f)));}catch{res.writeHead(500).end();}});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const origin=`http://127.0.0.1:${server.address().port}`;
const browser=await chromium.launch({headless:true});
try{
  const page=await browser.newPage({viewport:{width:1280,height:900}});
  const errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/api/billing/candidates',route=>route.fulfill({json:{status:'billing_candidates_loaded',generatedAt:new Date().toISOString(),approvedStatuses:[],canCreateInvoices:true,scope:'test',connectorStatuses:[],count:0,candidates:[]}}));
  await page.goto(origin);
  await page.waitForTimeout(300);
  const body=await page.locator('body').innerText();
  if(errors.length) throw new Error('pageerror: '+errors.join(' | '));
  if(body.includes('This page could not finish rendering')) throw new Error('error boundary rendered');
  if(!body.includes('Invoice & Billing Center')) throw new Error('invoice center did not render');
  console.log('INVOICE_EMPTY_STATE_BROWSER=PASS');
} finally {
  await browser.close();
  await new Promise(resolve=>server.close(resolve));
  await rm(output,{recursive:true,force:true});
}
