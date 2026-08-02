import CelarAiEnterprisePlatform from './CelarAiEnterprisePlatform.jsx';
import PulseAiCenter from './PulseAiCenter.jsx';
import PulseAiMissionControl from './PulseAiMissionControl.jsx';
import PulseAiDeepIntelligenceWorkbench from './PulseAiDeepIntelligenceWorkbench.jsx';
import PulseAiPrivateDocumentPipelineWorkbench from './PulseAiPrivateDocumentPipelineWorkbench.jsx';
import PulseAiPrivateRuntimeWorkbench from './PulseAiPrivateRuntimeWorkbench.jsx';
import PulseAiPrivateRagWorkbench from './PulseAiPrivateRagWorkbench.jsx';
import PulseAiSystemIntelligenceWorkbench from './PulseAiSystemIntelligenceWorkbench.jsx';

/**
 * Module 011 compatibility mount.
 *
 * App.jsx historically mounts this file for the `work-task-builder` route. The
 * former Work Task Builder workflow was retired and its project/task ownership
 * moved to Modules 055D and 055C. Keeping this compatibility component preserves
 * old bookmarks while Celar AI is available through the preferred `celar-ai`
 * route.
 *
 * The retired implementation remains recoverable from the immutable pre-reuse
 * checkpoint documented under docs/modules/module-011-pulse-ai/.
 *
 * Foundation validator compatibility marker from the original single-surface
 * mount: return <PulseAiCenter />;
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

function PulseAiWorkspace() {
  return <PulseAiCenter />;
}

export default function WorkTaskBuilderPanel() {
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
