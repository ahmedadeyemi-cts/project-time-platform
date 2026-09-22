#!/usr/bin/env node
/* Source-derived, presentation-only fixtures. Never execute application code. */
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const ts = require(process.env.SURFACE_TYPESCRIPT_MODULE || 'typescript');
const root = path.resolve(process.argv[2] || path.join(__dirname, '..'));
const output = path.resolve(process.argv[3] || 'module-surface-fixtures.json');
const decodeJsx = text => text.replace(/&(#x[0-9a-f]+|#\d+|apos|quot|lt|gt|amp|nbsp);/gi,(_,entity)=>entity.startsWith('#')?String.fromCodePoint(parseInt(entity.slice(entity[1]?.toLowerCase()==='x'?2:1),entity[1]?.toLowerCase()==='x'?16:10)):({apos:"'",quot:'"',lt:'<',gt:'>',amp:'&',nbsp:'\u00a0'})[entity.toLowerCase()]);
const escape = s => String(s).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;');
const files = dir => fs.readdirSync(dir,{withFileTypes:true}).flatMap(e => e.isDirectory()?files(path.join(dir,e.name)) : [path.join(dir,e.name)]);
const fixtures=[]; const inventory=[]; const seen=new Set();
const native = n => ts.isJsxElement(n) || ts.isJsxSelfClosingElement(n);
const open = n => ts.isJsxElement(n) ? n.openingElement : n;
const tag = n => open(n).tagName.getText();
let source, substitutions;
let resolutionDepth=0;
function defaultValue(n, mode) {
 if(resolutionDepth>8)return '';
 for(let parent=n.parent;parent;parent=parent.parent){
  if(!ts.isFunctionLike(parent))continue;
  let initializer;
  for(const parameter of parent.parameters || []){
   function lookup(x){if(ts.isBindingElement(x)&&x.name.getText()===n.text&&x.initializer)initializer=x.initializer;ts.forEachChild(x,lookup);}
   lookup(parameter);
  }
  if(initializer){resolutionDepth++;try{return value(initializer,mode);}finally{resolutionDepth--;}}
 }
 return '';
}
function value(n, mode=0) {
 if (!n) return '';
 if (ts.isParenthesizedExpression(n)) return value(n.expression,mode);
 if (ts.isStringLiteral(n) || ts.isNoSubstitutionTemplateLiteral(n) || ts.isNumericLiteral(n)) return n.text;
 if (n.kind===ts.SyntaxKind.TrueKeyword) return 'true';
 if (n.kind===ts.SyntaxKind.FalseKeyword) return 'false';
 if (ts.isIdentifier(n))return defaultValue(n,mode);
 if (ts.isJsxExpression(n)) return value(n.expression,mode);
 if (ts.isConditionalExpression(n)) return value(mode?n.whenFalse:n.whenTrue,mode);
 if (ts.isTemplateExpression(n)) return n.head.text+n.templateSpans.map(s=>value(s.expression,mode)+s.literal.text).join('');
 if(ts.isCallExpression(n)&&ts.isPropertyAccessExpression(n.expression)&&n.expression.name.text==='join') {
   let array=n.expression.expression;
   if(ts.isCallExpression(array)&&ts.isPropertyAccessExpression(array.expression)&&array.expression.name.text==='filter'&&array.arguments[0]?.getText()==='Boolean')array=array.expression.expression;
   if(ts.isArrayLiteralExpression(array))return array.elements.map(e=>value(e,mode)).filter(Boolean).join(n.arguments.length?value(n.arguments[0],mode):',');
 }
 if (ts.isBinaryExpression(n)) {
   if ([ts.SyntaxKind.BarBarToken,ts.SyntaxKind.QuestionQuestionToken].includes(n.operatorToken.kind)) return value(n.right,mode);
   if (n.operatorToken.kind===ts.SyntaxKind.AmpersandAmpersandToken) return value(n.right,mode);
   if (n.operatorToken.kind===ts.SyntaxKind.PlusToken) return value(n.left,mode)+value(n.right,mode);
 }
 return '';
}
function attrs(n, mode=0) {
 const a={};
 for(const p of open(n).attributes.properties){
   if(!ts.isJsxAttribute(p)) continue;
   const name=p.name.text;
   if(name.startsWith('on') || ['key','ref','dangerouslySetInnerHTML'].includes(name)) continue;
   if(!p.initializer) {if(['disabled','checked','multiple','open'].includes(name))a[name]=''; continue;}
   let v=value(p.initializer,mode);
   if(name==='style' && ts.isJsxExpression(p.initializer) && p.initializer.expression && ts.isObjectLiteralExpression(p.initializer.expression)) {
     v=p.initializer.expression.properties.filter(ts.isPropertyAssignment).map(prop=>{
       const k=prop.name.getText().replace(/^['"]|['"]$/g,'').replace(/[A-Z]/g,c=>'-'+c.toLowerCase());
       let vv=value(prop.initializer,mode); if(!vv)return '';
       if(ts.isNumericLiteral(prop.initializer) && !/^(opacity|z-index|flex|font-weight|order|line-height)$/.test(k))vv+='px';
       return k+':'+vv;
     }).filter(Boolean).join(';');
   }
   if(['disabled','checked'].includes(name)) {if(v==='true'||(!v&&mode===0))a[name]='';continue;}
   if(name==='src') {if(v.startsWith('data:image/svg'))a[name]=v;continue;}
   if(name==='href') {a.href='#audit';continue;}
   if(name==='className') a.class=v;
   else if(['id','title','role','type','value','placeholder','rows','colSpan','rowSpan','style','alt','viewBox','d','fill','stroke'].includes(name)||name.startsWith('data-')||name.startsWith('aria-')) {if(v)a[name]=v;}
 }
 return a;
}
const classes = n => attrs(n).class || '';
function toattrs(a) {return Object.entries(a).map(([k,v])=>` ${k}="${escape(v)}"`).join('');}
function render(n,mode=0,depth=0) {
 if(!n || depth>45)return '';
 if(ts.isParenthesizedExpression(n))return render(n.expression,mode,depth+1);
 if(ts.isJsxText(n)) return escape(decodeJsx(n.text.replace(/\s+/g,' ')));
 if(ts.isJsxFragment(n))return n.children.map(x=>render(x,mode,depth+1)).join('');
 if(ts.isJsxExpression(n))return render(n.expression,mode,depth+1);
 if(native(n)) {
   const t=tag(n); const a=attrs(n,mode);
   if(t==='svg'||['script','iframe','canvas','video','audio'].includes(t))return '';
   if(t==='img')return ''; // Images require separate integrity checks; not text fixtures.
   let actual=t;
   if(!/^[a-z][a-z0-9-]*$/.test(t)) {substitutions.add('component:'+t);return ''; /* Custom components retain their own DOM contract; do not flatten them. */ }
   let children=ts.isJsxElement(n)?n.children.map(x=>render(x,mode,depth+1)).join(''):'';
   if(['input','select','textarea'].includes(t)) {if(!a.value && t==='input')a.value='Sample';}
   const html=`<${actual}${toattrs(a)}>`;
   return html + (['input','br','hr','wbr','area','source'].includes(t)?'':children+`</${actual}>`);
 }
 if(ts.isConditionalExpression(n))return render(mode?n.whenFalse:n.whenTrue,mode,depth+1);
 if(ts.isBinaryExpression(n)) {
  if(n.operatorToken.kind===ts.SyntaxKind.AmpersandAmpersandToken){
   // The header closes its other overlays when opening a menu. Render the
   // More and profile surfaces independently, not as overlapping mock menus.
   if(n.left.getText(source)==='isProfileMenuOpen'&&mode===0){substitutions.add('exclusive-header-overlay:more');return '';}
   return render(n.right,mode,depth+1);
  }
  if([ts.SyntaxKind.BarBarToken,ts.SyntaxKind.QuestionQuestionToken].includes(n.operatorToken.kind))return render(n.right,mode,depth+1);
 }
 if(ts.isCallExpression(n)) {
  for(const a of n.arguments)if(ts.isArrowFunction(a)||ts.isFunctionExpression(a)) {
   if(ts.isBlock(a.body)){let returned;ts.forEachChild(a.body,c=>{if(ts.isReturnStatement(c))returned=c.expression;});return render(returned,mode,depth+1);}
   return render(a.body,mode,depth+1);
  }
 }
 if(n.kind===ts.SyntaxKind.NullKeyword||n.kind===ts.SyntaxKind.FalseKeyword||n.kind===ts.SyntaxKind.TrueKeyword)return '';
 const v=value(n,mode);
 if(v)return escape(v);
 if(/\.slice\(0,\s*2\)\.toUpperCase\(\)/.test(n.getText(source))){substitutions.add('bounded-initials:'+n.getText(source).slice(0,80));return 'AB';}
 substitutions.add('expression:'+n.getText(source).slice(0,90));
 return 'Sample';
}
function isSurface(n) {
 if(!native(n)||!['header','footer','div','section','aside'].includes(tag(n)))return false;
 const cls=classes(n);
 if(tag(n)==='header'||tag(n)==='footer')return true;
 return cls.split(/\s+/).some(c=>/(?:^|[-_])(hero|banner|callout|notice|alert|authority|empty|empty-state|status-card|status-panel|summary-card)$/.test(c));
}
for (const file of files(path.join(root,'src')).filter(f=>f.endsWith('.jsx')).sort()) {
 source=ts.createSourceFile(file,fs.readFileSync(file,'utf8'),ts.ScriptTarget.Latest,true,ts.ScriptKind.JSX);
 if(source.parseDiagnostics.length)throw new Error('Unparsed JSX: '+file);
 const relative=path.relative(root,file);let found=0;
 function walk(n,ancestors=[]) {
   if(isSurface(n)) {
    const outer=ancestors.filter(a=>native(a) && /^[a-z]/.test(tag(a)));
    // Exclude a surface already inside a captured surface; its descendants are covered there.
    if(!ancestors.some(isSurface)) {
     found++;
     for(let mode=0;mode<2;mode++){
      substitutions=new Set();
      const fragment=render(n,mode);
      if(!fragment.replace(/<[^>]*>/g,'').trim())continue;
      let html=fragment;
      for(const a of outer.slice().reverse()) {
       const sibling = c => {
        if(!native(c)||!/^[a-z]/.test(tag(c)))return '';
        const at=attrs(c,mode); at['data-audit-layout-placeholder']='true';
        at.style=(at.style?at.style+';':'')+'visibility:hidden!important';
        substitutions.add('layout-placeholder:'+tag(c)+'.'+(at.class||''));
        return `<${tag(c)}${toattrs(at)}>`+(['img','input','br','hr'].includes(tag(c))?'':`</${tag(c)}>`);
       };
       const children=ts.isJsxElement(a)?a.children:[];
       const before=children.filter(c=>c.end<=n.pos).map(sibling).join('');
       const after=children.filter(c=>c.pos>=n.end).map(sibling).join('');
       html=`<${tag(a)}${toattrs(attrs(a,mode))}>${before}${html}${after}</${tag(a)}>`;
      }
      const key=html;
      if(seen.has(key))continue; seen.add(key);
      fixtures.push({file:relative,line:source.getLineAndCharacterOfPosition(n.getStart()).line+1,classes:classes(n),tag:tag(n),branch:mode,substitutions:[...substitutions],html});
     }
    }
   }
   ts.forEachChild(n,c=>walk(c,[...ancestors,n]));
 }
 walk(source);
 // Components without a header/feedback surface still receive a native-root
 // fixture. Custom-component roots are recorded but are never flattened.
 if(found===0){
  function fallback(n,parents=[]){
   if(native(n)&&/^[a-z]/.test(tag(n))&&!parents.some(native)){
    found++;
    for(let mode=0;mode<2;mode++){
     substitutions=new Set(['native-root-fallback']); const html=render(n,mode);
     if(!html.replace(/<[^>]*>/g,'').trim()||seen.has(html))continue;
     seen.add(html); fixtures.push({file:relative,line:source.getLineAndCharacterOfPosition(n.getStart()).line+1,classes:classes(n),tag:tag(n),branch:mode,substitutions:[...substitutions],html});
    }
   }
   ts.forEachChild(n,c=>fallback(c,[...parents,n]));
  }
  fallback(source);
 }
 inventory.push({file:relative,surfaces:found,sha256:crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')});
}
// Explicit source-owned state variants supplement unresolved dynamic classes.
const variants=[
 {className:'m025-generation-phase',prefix:'is-',states:['running','retrying','completed','resumed','failed','interrupted']},
 {className:'m025-notice',prefix:'m025-notice--',states:['critical','warning']}
];
for(const variant of variants){
 const candidate=fixtures.find(f=>new RegExp('class="[^"]*\\b'+variant.className+'\\b').test(f.html));
 if(!candidate)throw new Error('Missing required state fixture: '+variant.className);
 for(const state of variant.states){
  const html=candidate.html.replace(/class="([^"]*)"/g,(whole,classes)=>{
   const names=classes.split(/\s+/);if(!names.includes(variant.className))return whole;
   return 'class="'+names.filter(c=>!c.startsWith(variant.prefix)).concat(variant.prefix+state).join(' ')+'"';
  });
  fixtures.push({...candidate,branch:'state:'+state,substitutions:[...candidate.substitutions,'explicit-source-state:'+state],html});
 }
}
// Use only the reviewed DOM-only route-header adapter, not React or app code.
const adapterFile=path.join(root,'src/CriticalRoutePresentationBoundary.jsx');
const adapterText=fs.readFileSync(adapterFile,'utf8');
const adapterSha256=crypto.createHash('sha256').update(adapterText).digest('hex');
if(adapterSha256!=='6ab096d751dd46d686ae5176612256aa262bfde4bad584af7af82d4b487506ae')
 throw new Error('DOM-only adapter changed; review its behavior before refreshing the pin.');
const adapterAst=ts.createSourceFile(adapterFile,adapterText,ts.ScriptTarget.Latest,true,ts.ScriptKind.JSX);
const adapterNames=new Set(['ENTERPRISE_HEADER_ATTRIBUTE','ENTERPRISE_HEADER_OWNER_ATTRIBUTE','MODULE_ROOT_SELECTORS','ROUTE_ROOT_SELECTORS','MODULE_ROOT_SELECTOR','ROUTE_HEADER_SELECTOR','EXCLUDED_HEADER_ANCESTORS','isRoutePageHeader','adoptEnterprisePageHeaders']);
const adapterParts=[];const adapterFound=new Set();
for(const statement of adapterAst.statements){
 const names=ts.isVariableStatement(statement)?statement.declarationList.declarations.map(d=>d.name.getText()):ts.isFunctionDeclaration(statement)?[statement.name?.text]:[];
 if(names.some(n=>adapterNames.has(n))){
  if(names.some(n=>!adapterNames.has(n)))throw new Error('Unexpected combined DOM-adapter declaration');
  names.forEach(n=>adapterFound.add(n));adapterParts.push(statement.getText(adapterAst));
 }
}
if(adapterFound.size!==adapterNames.size)throw new Error('DOM adapter changed; renew surface fixture review');
const domAdoption='() => {'+adapterParts.join('\n')+"\nadoptEnterprisePageHeaders(document.getElementById('audit-root'));}";
if(!fixtures.length)throw new Error('No source-derived fixtures were generated.');
for(const name of ['oncall-authority','oncall-quick-links','vendor-directory-authority'])
 if(!fixtures.some(f=>f.html.includes(name)))throw new Error('Required reported regression surface is absent: '+name);
fs.mkdirSync(path.dirname(output),{recursive:true});
fs.writeFileSync(output,JSON.stringify({scope:'Source-derived isolated JSX presentation with the real DOM-only header adapter. Dynamic values substituted; no business code executed.',domAdoption,adapterSha256,inventory,fixtures},null,2)+'\n');
console.log(JSON.stringify({jsxFiles:inventory.length,fixtures:fixtures.length,filesWithSurfaces:inventory.filter(f=>f.surfaces).length}));
