import IdentityPresence from './IdentityPresence.jsx';

function initials(value) {
  const words = String(value || '')
    .trim()
    .split(/\s+/)
    .filter(Boolean);

  if (!words.length) {
    return '?';
  }

  if (words.length === 1) {
    return words[0].slice(0, 2).toUpperCase();
  }

  return `${words[0][0] || ''}${words.at(-1)?.[0] || ''}`
    .toUpperCase();
}

export default function IdentityAvatar({
  profile,
  presence = profile?.presence,
  size = 'medium',
  showPresence = true,
  className = ''
}) {
  const displayName =
    profile?.displayName
    || profile?.name
    || profile?.email
    || 'ProjectPulse user';

  const photo =
    profile?.profilePhotoDataUrl
    || profile?.profilePhoto
    || profile?.photoUrl
    || '';

  return (
    <div
      className={[
        'identity-avatar',
        `identity-avatar-${size}`,
        className
      ].filter(Boolean).join(' ')}
    >
      {photo ? (
        <img
          src={photo}
          alt={`${displayName} profile`}
        />
      ) : (
        <span className="identity-avatar-initials">
          {initials(displayName)}
        </span>
      )}

      {showPresence ? (
        <IdentityPresence
          presence={presence}
          compact
          className="identity-avatar-presence"
        />
      ) : null}
    </div>
  );
}
