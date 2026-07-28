import { useState } from 'react';
import OperationalEvidenceCenter from './OperationalEvidenceCenter.jsx';
import LegacyBackupRetentionCenter from './LegacyBackupRetentionCenter.jsx';
import './operational-evidence-center.css';

export default function BackupRetentionCenter({ authSession }) {
  const [view, setView] = useState('evidence');

  return (
    <section id="backup-retention-center" className="panel module-016-operational-center" data-module="016">
      <nav className="module-016-view-switcher" aria-label="Module 016 views">
        <button
          type="button"
          className={view === 'evidence' ? 'active' : ''}
          aria-pressed={view === 'evidence'}
          onClick={() => setView('evidence')}
        >
          <strong>Operational Evidence</strong>
          <span>Logs, failures, workers, jobs, correlations, and exports</span>
        </button>
        <button
          type="button"
          className={view === 'backups' ? 'active' : ''}
          aria-pressed={view === 'backups'}
          onClick={() => setView('backups')}
        >
          <strong>Backup Retention</strong>
          <span>Preserved backup inventory and guarded cleanup</span>
        </button>
      </nav>

      {view === 'evidence'
        ? <OperationalEvidenceCenter authSession={authSession} />
        : <LegacyBackupRetentionCenter authSession={authSession} />}
    </section>
  );
}
