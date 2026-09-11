import { useState } from 'react';
import SowGsdAuthoringWorkspace from './SowGsdAuthoringWorkspace.jsx';
import SowRegister from './SowRegister.jsx';
import './sow-register.css';

export default function SowGsdWorkspace() {
  const [view, setView] = useState('authoring');
  return (
    <div className="m025-workspace m025-shell">
      <nav className="m025-tabs" aria-label="SOW authoring and retained records" role="tablist">
        <button type="button" id="m025-authoring-tab" role="tab" aria-selected={view === 'authoring'} aria-controls="m025-authoring-panel"
          className={view === 'authoring' ? 'is-active' : ''} onClick={() => setView('authoring')}>SOW Authoring</button>
        <button type="button" id="m025-register-tab" role="tab" aria-selected={view === 'register'} aria-controls="m025-register-panel"
          className={view === 'register' ? 'is-active' : ''} onClick={() => setView('register')}>SOW Register &amp; SELL</button>
      </nav>
      {/* Keep the existing editor mounted: switching tabs must not discard
          unsaved SA edits or stop its already-running generation status checks. */}
      <div id="m025-authoring-panel" role="tabpanel" aria-labelledby="m025-authoring-tab" hidden={view !== 'authoring'}>
        <SowGsdAuthoringWorkspace />
      </div>
      <div id="m025-register-panel" role="tabpanel" aria-labelledby="m025-register-tab" hidden={view !== 'register'}>
        {view === 'register' ? <SowRegister /> : null}
      </div>
    </div>
  );
}
