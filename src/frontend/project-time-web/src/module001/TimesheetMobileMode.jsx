import { useEffect, useState } from 'react';

const STORAGE_KEY = 'projectPulseTimesheetMobileMode';

export default function TimesheetMobileMode({ onChange = () => {} }) {
  const [enabled, setEnabled] = useState(() => {
    try { return window.localStorage.getItem(STORAGE_KEY) === 'true'; } catch { return false; }
  });

  useEffect(() => {
    document.documentElement.classList.toggle('projectpulse-timesheet-mobile-mode', enabled);
    try { window.localStorage.setItem(STORAGE_KEY, String(enabled)); } catch { /* presentation preference only */ }
    onChange(enabled);
    return () => document.documentElement.classList.remove('projectpulse-timesheet-mobile-mode');
  }, [enabled, onChange]);

  return (
    <label className="module001-mobile-toggle">
      <input type="checkbox" checked={enabled} onChange={(event) => setEnabled(event.target.checked)} />
      <span>Mobile mode</span>
    </label>
  );
}
