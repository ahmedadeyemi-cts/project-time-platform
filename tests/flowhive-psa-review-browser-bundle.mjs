// Compile the actual reviewer, with synthetic callbacks supplied by the browser test.
// No server, application account, provider call or external network is involved.
import {fileURLToPath} from 'node:url';
import path from 'node:path';
import fs from 'node:fs';
const root=fileURLToPath(new URL('../src/frontend/project-time-web/',import.meta.url));
const {build}=await import(path.join(root,'node_modules/vite/dist/node/index.js'));
const {default:react}=await import(path.join(root,'node_modules/@vitejs/plugin-react/dist/index.js'));
const entry='virtual:flowhive-review-test';
const result=await build({root,configFile:false,define:{'process.env.NODE_ENV':JSON.stringify('production')},plugins:[react(),{
  name:'flowhive-review-test-entry',resolveId(id){if(id===entry || id.endsWith('/'+entry))return '\0'+entry;},
  load(id){if(id==='\0'+entry)return `import React from ${JSON.stringify(path.join(root,'node_modules/react/index.js'))};import{createRoot}from ${JSON.stringify(path.join(root,'node_modules/react-dom/client.js'))};import Review from ${JSON.stringify(path.join(root,'src/ProjectFlowHivePlannerReview.jsx'))};const root=createRoot(document.getElementById('root'));window.mountFlowHiveReview=props=>root.render(React.createElement(Review,props));`;}
}],build:{write:false,lib:{entry,formats:['iife'],name:'FlowHiveReviewTest'},cssCodeSplit:false,minify:false}});
const output=(Array.isArray(result)?result:[result]).flatMap(item=>item.output);
const out=process.env.FLOWHIVE_REVIEW_BUNDLE||'/tmp/flowhive-review-test';fs.mkdirSync(out,{recursive:true});
fs.writeFileSync(path.join(out,'app.js'),output.filter(item=>item.type==='chunk').map(item=>item.code).join('\n'));
fs.writeFileSync(path.join(out,'app.css'),output.filter(item=>item.fileName.endsWith('.css')).map(item=>item.source).join('\n'));
console.log('FLOWHIVE_OFFLINE_REVIEW_COMPONENT_BUNDLE_READY');
