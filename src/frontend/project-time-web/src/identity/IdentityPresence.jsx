import {
  normalizePresence
} from './presence.js';
import './identity-profile.css';

export default function IdentityPresence({
  presence,
  compact = false,
  className = ''
}) {
  const normalized = normalizePresence(presence);

  return (
    <span
      className={[
        'identity-presence',
        `presence-${normalized.cssState}`,
        compact ? 'compact' : '',
        className
      ].filter(Boolean).join(' ')}
      data-presence-state={normalized.state}
      data-presence-availability={normalized.availability}
      title={normalized.label}
      aria-label={`Presence: ${normalized.label}`}
    >
      <span
        className="identity-presence-dot"
        aria-hidden="true"
      />
      <span className="identity-presence-label">
        {normalized.label}
      </span>
    </span>
  );
}
