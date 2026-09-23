import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
const web=fileURLToPath(new URL('../',import.meta.url));
const index=process.argv.indexOf('--output');
if(index<0 || !process.argv[index+1]) throw new Error('Expected --output directory');
const output=path.resolve(process.argv[index+1]);fs.mkdirSync(output,{recursive:true});
const builtIndex=fs.readFileSync(path.join(web,'dist/index.html'),'utf8');
const links=[...builtIndex.matchAll(/<link\b[^>]*rel=["']stylesheet["'][^>]*>/g)].map(match=>match[0]);
if(!links.length) throw new Error('A complete production build is required.');
for(const link of links){
  const href=link.match(/href=["']([^"']+)["']/)?.[1];
  if(!href?.startsWith('/assets/') || href.includes('..')) throw new Error('Unexpected stylesheet path');
  const target=path.join(output,href);fs.mkdirSync(path.dirname(target),{recursive:true});fs.copyFileSync(path.join(web,'dist',href),target);
}
const fixture=`import React from 'react';
import { createRoot } from 'react-dom/client';
import SystemUserGuide from './src/SystemUserGuide.Module001.g.jsx';
import { ROLE_REFERENCE } from './src/guide/guide-content.js';
window.__guideTestRoleCodes=ROLE_REFERENCE.map(r=>r.code);
window.__guideTestAuthority=(payload={})=>{
 const roles=payload.roles || ['ENGINEERING'];
 localStorage.setItem('projectPulseAuthSession',JSON.stringify({userId:payload.identity || 'offline-guide-reader',roleCodes:roles,sessionToken:'not-a-real-session-offline-fixture'}));
 if(payload.viewAs) localStorage.setItem('projectPulseViewAsUser',JSON.stringify({userId:'offline-effective-reader',roleCodes:roles})); else localStorage.removeItem('projectPulseViewAsUser');
 if(!payload.preserveNavigation) window.__projectPulseEffectiveNavigation={state:payload.state || 'ready',roleCodes:roles,journeyRoleCodes:roles,isViewAs:Boolean(payload.viewAs),evidenceContract:'projectpulse-rbac-v1',refreshFailed:Boolean(payload.refreshFailed),deniedModuleNumbers:[],retiredModuleNumbers:[]};
 window.__projectPulseAuthorizedWorkspaceNavigation={contract:'SHARED_WORKSPACE_MODULE_AUTHORITY_V1',state:'ready',moduleNumbers:payload.modules || ['001','999'],viewAsUserId:payload.viewAs ? 'offline-effective-reader' : ''};
 window.dispatchEvent(new Event('projectpulse:permission-navigation-updated'));
};
window.__guideTestAuthority();
createRoot(document.getElementById('root')).render(<SystemUserGuide />);`;
await build({stdin:{contents:fixture,resolveDir:web,sourcefile:'user-guide-offline-fixture.jsx',loader:'jsx'},bundle:true,format:'iife',jsx:'automatic',loader:{'.css':'empty'},outfile:path.join(output,'app.js'),define:{'process.env.NODE_ENV':'"production"'},logLevel:'warning'});
fs.writeFileSync(path.join(output,'index.html'),`<!doctype html><html data-theme="light" data-pulse-experience="enterprise" data-pulse-layout="table"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">${links.join('')}</head><body><main class="app-shell"><div id="root"></div></main><script src="/app.js"></script></body></html>`);
console.log('USER_GUIDE_BROWSER_FIXTURE='+output);
