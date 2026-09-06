// Build an offline test bundle of the actual React component. This never opens a network listener.
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import fs from 'node:fs';
const root=fileURLToPath(new URL('../src/frontend/project-time-web/',import.meta.url));
const {build}=await import(path.join(root,'node_modules/vite/dist/node/index.js'));
const {default:react}=await import(path.join(root,'node_modules/@vitejs/plugin-react/dist/index.js'));
const entry='virtual:flowhive-component-test';
const result=await build({root,configFile:false,define:{'process.env.NODE_ENV':JSON.stringify('production')},plugins:[react(),{
  name:'flowhive-component-entry',resolveId(id){if(id===entry || id.endsWith('/'+entry))return '\0'+entry;},
  load(id){if(id==='\0'+entry)return `import React from ${JSON.stringify(path.join(root,'node_modules/react/index.js'))};import{createRoot}from ${JSON.stringify(path.join(root,'node_modules/react-dom/client.js'))};import FlowHive from ${JSON.stringify(path.join(root,'src/ProjectFlowHiveCenter.jsx'))};createRoot(document.getElementById('root')).render(React.createElement(FlowHive));`;}
}],build:{write:false,lib:{entry,formats:['iife'],name:'FlowHiveComponentTest'},cssCodeSplit:false,minify:false}});
const output=(Array.isArray(result)?result:[result]).flatMap(item=>item.output);
const out=process.env.FLOWHIVE_TEST_BUNDLE||'/tmp/flowhive-react-test';fs.mkdirSync(out,{recursive:true});
fs.writeFileSync(path.join(out,'app.js'),output.filter(item=>item.type==='chunk').map(item=>item.code).join('\n'));
fs.writeFileSync(path.join(out,'app.css'),output.filter(item=>item.fileName.endsWith('.css')).map(item=>item.source).join('\n'));
console.log('FLOWHIVE_OFFLINE_COMPONENT_BUNDLE_READY');
