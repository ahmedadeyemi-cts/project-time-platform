import { createServer } from '../src/frontend/project-time-web/node_modules/vite/dist/node/index.js';
import react from '../src/frontend/project-time-web/node_modules/@vitejs/plugin-react/dist/index.js';
const server=await createServer({configFile:false,root:process.cwd()+'/src/frontend/project-time-web',server:{host:'127.0.0.1',port:0},plugins:[react(),{
  name:'catalog-browser-fixture',
  resolveId(id){if(id==='/catalog-entry.js')return '\0catalog-entry';},
  load(id){if(id==='\0catalog-entry')return `import React from 'react'; import {createRoot} from 'react-dom/client'; import Panel from '/src/RoleAdminDirectoryPanel.jsx'; createRoot(document.getElementById('root')).render(React.createElement(Panel));`;},
  configureServer(vite){vite.middlewares.use(async(req,res,next)=>{if(req.url!=='/')return next();res.setHeader('Content-Type','text/html');res.end(await vite.transformIndexHtml('/', '<div id="root"></div><script type="module" src="/catalog-entry.js"></script>'));});}
}]});
await server.listen();console.log(`CATALOG_BROWSER_READY=http://127.0.0.1:${server.httpServer.address().port}`);
