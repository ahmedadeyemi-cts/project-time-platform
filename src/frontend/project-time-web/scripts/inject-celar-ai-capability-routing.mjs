import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const target = path.join(root, 'src', 'AiProviderConfigurationCenter.jsx');
let content = fs.readFileSync(target, 'utf8');

function replaceRequired(before, after, label) {
  if (content.includes(after)) return;
  if (!content.includes(before)) throw new Error(`CELAR_AI_ROUTING_MISSING_ANCHOR=${label}`);
  content = content.replace(before, after);
}

replaceRequired(
  `import CelarAiProviderBridgePanel from './CelarAiProviderBridgePanel.jsx';\n`,
  `import CelarAiProviderBridgePanel from './CelarAiProviderBridgePanel.jsx';\nimport CelarAiCapabilityRoutingPanel from './CelarAiCapabilityRoutingPanel.jsx';\n`,
  'provider_routing_import');

replaceRequired(
  `          <CelarAiProviderBridgePanel />\n\n          <section className="ai-provider-center__section">`,
  `          <CelarAiProviderBridgePanel />\n          <CelarAiCapabilityRoutingPanel />\n\n          <section className="ai-provider-center__section">`,
  'provider_routing_mount');

fs.writeFileSync(target, content, 'utf8');
console.log('CELAR_AI_CAPABILITY_ROUTING_UI=INJECTED');
console.log('CELAR_AI_CAPABILITY_ROUTING_DEFAULT=celar_ai,claude,openai,local_template');
console.log('CELAR_AI_CAPABILITY_ROUTING_PRIVACY_POLICY_EDITABLE=NO');
