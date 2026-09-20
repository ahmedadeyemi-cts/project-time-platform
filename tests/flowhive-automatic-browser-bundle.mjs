import { fileURLToPath } from 'node:url';
import path from 'node:path';
import fs from 'node:fs';
const root=fileURLToPath(new URL('../src/frontend/project-time-web/',import.meta.url));
const {build}=await import(path.join(root,'node_modules/vite/dist/node/index.js'));
const {default:react}=await import(path.join(root,'node_modules/@vitejs/plugin-react/dist/index.js'));
const entry='virtual:flowhive-automatic-test';
const result=await build({root,configFile:false,define:{'process.env.NODE_ENV':JSON.stringify('production')},plugins:[react(),{
  name:'automatic-fixture',resolveId(id){if(id===entry||id.endsWith('/'+entry))return '\0'+entry;},
  load(id){if(id==='\0'+entry)return `import React,{useState}from'react';import{createRoot}from'react-dom/client';import Panel from ${JSON.stringify(path.join(root,'src/ProjectFlowHiveAutomation.jsx'))};
  async function json(url,options){const r=await fetch(url,options);const v=await r.json();if(!r.ok){const e=new Error(v.message);e.responseBody=v;throw e;}return v;}
  const getJson=(url,signal)=>json(url,{signal});const putJson=(url,body)=>json(url,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
  function App(){const[project,setProject]=useState('project-a');window.setAutomationProject=setProject;return React.createElement(Panel,{key:project,projectId:project,getJson,putJson,onLoadDraft:()=>{window.draftLoads=(window.draftLoads||0)+1;}});}createRoot(document.getElementById('root')).render(React.createElement(App));`;}
}],build:{write:false,lib:{entry,formats:['iife'],name:'AutomaticFixture'},cssCodeSplit:false,minify:false}});
const output=(Array.isArray(result)?result:[result]).flatMap(item=>item.output);
const out=process.env.FLOWHIVE_AUTOMATIC_BUNDLE||'/tmp/flowhive-automatic-test';fs.mkdirSync(out,{recursive:true});
fs.writeFileSync(path.join(out,'app.js'),output.filter(item=>item.type==='chunk').map(item=>item.code).join('\n'));
fs.writeFileSync(path.join(out,'app.css'),output.filter(item=>item.fileName.endsWith('.css')).map(item=>item.source).join('\n'));
console.log('FLOWHIVE_AUTOMATIC_BROWSER_BUNDLE=PASS');
