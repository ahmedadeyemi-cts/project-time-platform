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
 * moved to Modules 055D and 055C. Keeping this small compatibility component
 * avoids a shared App.jsx edit while Module 011 is reactivated as Celar AI.
 *
 * The retired implementation remains recoverable from the immutable pre-reuse
 * checkpoint documented under docs/modules/module-011-pulse-ai/.
 *
 * Foundation validator compatibility marker from the original single-surface
 * mount: return <PulseAiCenter />;
 */
export {
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
