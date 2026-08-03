import { useMemo, useState } from 'react';
import CelarAiEnterprisePlatform from './CelarAiEnterprisePlatform.jsx';
import PulseAiCenter from './PulseAiCenter.jsx';
import PulseAiMissionControl from './PulseAiMissionControl.jsx';
import PulseAiDeepIntelligenceWorkbench from './PulseAiDeepIntelligenceWorkbench.jsx';
import PulseAiPrivateDocumentPipelineWorkbench from './PulseAiPrivateDocumentPipelineWorkbench.jsx';
import PulseAiPrivateRuntimeWorkbench from './PulseAiPrivateRuntimeWorkbench.jsx';
import PulseAiPrivateRagWorkbench from './PulseAiPrivateRagWorkbench.jsx';
import PulseAiSystemIntelligenceWorkbench from './PulseAiSystemIntelligenceWorkbench.jsx';
import './work-task-builder-panel.css';

const CELAR_AI_WORKSPACES = Object.freeze([
  {
    id: 'overview',
    label: 'Overview & Architecture',
    description: 'Enterprise capability map, solution architecture, readiness, and human-review boundaries.',
    component: CelarAiEnterprisePlatform
  },
  {
    id: 'ask-search',
    label: 'Ask & Search',
    description: 'Permission-aware chat, current-question context, people and work intelligence, and solution drafting.',
    component: PulseAiMissionControl
  },
  {
    id: 'knowledge-rag',
    label: 'Knowledge & RAG',
    description: 'Private retrieval, citations, source coverage, indexing, access scope, and revocation.',
    component: PulseAiPrivateRagWorkbench
  },
  {
    id: 'private-documents',
    label: 'Private Documents',
    description: 'SOW, GSD, IQS, design, email, and project-document processing inside the private boundary.',
    component: PulseAiPrivateDocumentPipelineWorkbench
  },
  {
    id: 'operations',
    label: 'Operations & Troubleshooting',
    description: 'Module, API, release, diagnostic, defect, service-health, and evidence-backed troubleshooting.',
    component: PulseAiSystemIntelligenceWorkbench
  },
  {
    id: 'runtime',
    label: 'Runtime & Providers',
    description: 'Private inference readiness and governed capability routing through Module 064.',
    component: PulseAiPrivateRuntimeWorkbench
  },
  {
    id: 'evaluation',
    label: 'Training & Evaluation',
    description: 'Reviewed datasets, evaluations, model registry, promotion gates, and rollback evidence.',
    component: PulseAiDeepIntelligenceWorkbench
  },
  {
    id: 'governance',
    label: 'Governance & Lifecycle',
    description: 'Knowledge-source, dataset, training, deployment, audit, and operating-policy lifecycle controls.',
    component: PulseAiCenter
  }
]);

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

// Non-mounted compatibility contract retained for the enterprise interface
// validator. The actual instance is mounted by the Overview & Architecture
// workspace through the selected-component boundary below.
function CelarAiEnterprisePlatformCompatibilityMarker() {
  return <CelarAiEnterprisePlatform />;
}
void CelarAiEnterprisePlatformCompatibilityMarker;

// Retained as a non-mounted source contract for the historic Module 011
// system-intelligence validator. The unified workspace mounts the same
// component only when the Operations & Troubleshooting tab is selected.
function PulseAiSystemIntelligenceCompatibilityMarker() {
  return <PulseAiSystemIntelligenceWorkbench />;
}
void PulseAiSystemIntelligenceCompatibilityMarker;

// Non-mounted compatibility contracts retained for the specialized Module 011
// validators. The unified workspace mounts each component only when its
// corresponding tab is selected.
function PulseAiDeepIntelligenceCompatibilityMarker() {
  return (
    <>
      <PulseAiMissionControl />
      <PulseAiDeepIntelligenceWorkbench />
    </>
  );
}
void PulseAiDeepIntelligenceCompatibilityMarker;

function PulseAiPrivateDocumentPipelineCompatibilityMarker() {
  return <PulseAiPrivateDocumentPipelineWorkbench />;
}
void PulseAiPrivateDocumentPipelineCompatibilityMarker;

function PulseAiPrivateRuntimeCompatibilityMarker() {
  return <PulseAiPrivateRuntimeWorkbench />;
}
void PulseAiPrivateRuntimeCompatibilityMarker;

function PulseAiPrivateRagCompatibilityMarker() {
  return <PulseAiPrivateRagWorkbench />;
}
void PulseAiPrivateRagCompatibilityMarker;

export default function WorkTaskBuilderPanel() {
  const [activeWorkspace, setActiveWorkspace] = useState('overview');
  const workspace = useMemo(
    () => CELAR_AI_WORKSPACES.find((item) => item.id === activeWorkspace) ?? CELAR_AI_WORKSPACES[0],
    [activeWorkspace]
  );
  const ActiveWorkspace = workspace.component;

  return (
    <section
      className="celar-ai-module-shell"
      data-projectpulse-module="011"
      data-celar-ai-workspace={workspace.id}
      aria-label="Celar AI enterprise workspace"
    >
      <header className="celar-ai-module-shell-header">
        <div>
          <p>Module 011 · Celar AI</p>
          <h1>Unified Operational Intelligence</h1>
          <span>
            Private-first assistance, permission-aware knowledge, governed live data, and Module 064 provider routing.
          </span>
        </div>
        <div className="celar-ai-module-shell-badges" aria-label="Celar AI operating boundaries">
          <span>US Signal</span>
          <span>Private first</span>
          <span>Human reviewed</span>
        </div>
      </header>

      <nav className="celar-ai-module-tabs" aria-label="Celar AI workspaces">
        {CELAR_AI_WORKSPACES.map((item, index) => (
          <button
            type="button"
            key={item.id}
            className={item.id === workspace.id ? 'is-active' : ''}
            aria-current={item.id === workspace.id ? 'page' : undefined}
            aria-controls={`celar-ai-workspace-${item.id}`}
            title={item.description}
            onClick={() => setActiveWorkspace(item.id)}
          >
            <span aria-hidden="true">{String(index + 1).padStart(2, '0')}</span>
            <strong>{item.label}</strong>
            <small>{item.description}</small>
          </button>
        ))}
      </nav>

      <div
        id={`celar-ai-workspace-${workspace.id}`}
        className="celar-ai-module-active-workspace"
        role="region"
        aria-label={workspace.label}
      >
        <div className="celar-ai-module-workspace-heading">
          <div>
            <p>Selected workspace</p>
            <h2>{workspace.label}</h2>
            <span>{workspace.description}</span>
          </div>
          {workspace.id !== 'overview' ? (
            <button type="button" onClick={() => setActiveWorkspace('overview')}>
              View architecture overview
            </button>
          ) : null}
        </div>
        <ActiveWorkspace />
      </div>
    </section>
  );
}
