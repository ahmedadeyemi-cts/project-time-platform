// Compile the actual team calendar, with synthetic callbacks supplied by the browser test.
// No server, application account, provider call or external network is involved.
import {fileURLToPath} from 'node:url';
import path from 'node:path';
import fs from 'node:fs';
const root=fileURLToPath(new URL('../src/frontend/project-time-web/',import.meta.url));
const {build}=await import(path.join(root,'node_modules/vite/dist/node/index.js'));
const {default:react}=await import(path.join(root,'node_modules/@vitejs/plugin-react/dist/index.js'));
const entry='virtual:flowhive-team-calendar-test';
const result=await build({root,configFile:false,define:{'process.env.NODE_ENV':JSON.stringify('production')},plugins:[react(),{
  name:'flowhive-team-calendar-test-entry',resolveId(id){if(id===entry || id.endsWith('/'+entry))return '\0'+entry;},
  load(id){if(id==='\0'+entry)return `import React from ${JSON.stringify(path.join(root,'node_modules/react/index.js'))};import{createRoot}from ${JSON.stringify(path.join(root,'node_modules/react-dom/client.js'))};import Review from ${JSON.stringify(path.join(root,'src/ProjectFlowHiveTeamCalendar.jsx'))};import Overview from ${JSON.stringify(path.join(root,'src/ProjectFlowHiveOverview.jsx'))};import Readiness from ${JSON.stringify(path.join(root,'src/ProjectFlowHiveDocumentReadiness.jsx'))};import Timer from ${JSON.stringify(path.join(root,'src/ai/AiOperationProgress.jsx'))};import ${JSON.stringify(path.join(root,'src/project-flowhive-center.css'))};import ${JSON.stringify(path.join(root,'src/project-flowhive-psa-workspace.css'))};const root=createRoot(document.getElementById('root'));window.mountFlowHiveTeamCalendar=props=>root.render(React.createElement(Review,props));window.mountFlowHiveOverview=props=>root.render(React.createElement(Overview,props));window.mountReadiness=props=>root.render(React.createElement(Readiness,{...props,key:props.projectId}));window.mountTimer=props=>root.render(React.createElement(Timer,props));`;}
}],build:{write:false,lib:{entry,formats:['iife'],name:'FlowHiveTeamCalendarTest'},cssCodeSplit:false,minify:false}});
const output=(Array.isArray(result)?result:[result]).flatMap(item=>item.output);
const out=process.env.FLOWHIVE_TEAM_CALENDAR_BUNDLE||'/tmp/flowhive-team-calendar-test';fs.mkdirSync(out,{recursive:true});
fs.writeFileSync(path.join(out,'app.js'),output.filter(item=>item.type==='chunk').map(item=>item.code).join('\n'));
fs.writeFileSync(path.join(out,'app.css'),output.filter(item=>item.fileName.endsWith('.css')).map(item=>item.source).join('\n'));
console.log('FLOWHIVE_TEAM_CALENDAR_COMPONENT_BUNDLE_READY');
