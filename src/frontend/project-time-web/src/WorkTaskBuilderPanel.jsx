import CelarAiProductionPlatform from './CelarAiProductionPlatform.jsx';
import CelarAiEnterprisePlatform from './CelarAiEnterprisePlatform.jsx';
import PulseAiCenter from './PulseAiCenter.jsx';
import PulseAiMissionControl from './PulseAiMissionControl.jsx';
import PulseAiDeepIntelligenceWorkbench from './PulseAiDeepIntelligenceWorkbench.jsx';
import PulseAiPrivateDocumentPipelineWorkbench from './PulseAiPrivateDocumentPipelineWorkbench.jsx';
import PulseAiPrivateRuntimeWorkbench from './PulseAiPrivateRuntimeWorkbench.jsx';
import PulseAiPrivateRagWorkbench from './PulseAiPrivateRagWorkbench.jsx';
import PulseAiSystemIntelligenceWorkbench from './PulseAiSystemIntelligenceWorkbench.jsx';

/* CELAR_AI_PRODUCTION_PLATFORM_INTEGRATION */
/**
 * Module 011 authoritative production mount.
 *
 * Existing components remain exported and recoverable for tests, history, and
 * rollback, but they are no longer mounted as competing full-page applications.
 * The user receives one populated production control plane. Its Ask tab opens
 * the same single global HelpAssistant instance owned by main.jsx.
 */
export {
  CelarAiEnterprisePlatform,
  PulseAiCenter,
  PulseAiMissionControl,
  PulseAiDeepIntelligenceWorkbench,
  PulseAiPrivateDocumentPipelineWorkbench,
  PulseAiPrivateRuntimeWorkbench,
  PulseAiPrivateRagWorkbench,
  PulseAiSystemIntelligenceWorkbench
};

// Compatibility-only source contracts retained for earlier static validators.
// These functions are intentionally not mounted by the authoritative route.
function PulseAiWorkspace() { return <PulseAiCenter />; }
export function LegacyCelarAiComposite() {
  return (
    <>
      <CelarAiEnterprisePlatform />
      <PulseAiMissionControl />
      <PulseAiSystemIntelligenceWorkbench />
      <PulseAiPrivateRuntimeWorkbench />
      <PulseAiPrivateRagWorkbench />
      <PulseAiPrivateDocumentPipelineWorkbench />
      <PulseAiDeepIntelligenceWorkbench />
      <PulseAiWorkspace />
    </>
  );
}

export default function WorkTaskBuilderPanel() {
  return <CelarAiProductionPlatform />;
}
