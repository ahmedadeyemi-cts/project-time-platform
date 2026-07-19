import IdentityAvatar from './IdentityAvatar.jsx';
import IdentityPresence from './IdentityPresence.jsx';

export default function IdentityProfileCard({
  profile,
  utilization,
  compact = false,
  showCapacity = false,
  className = ''
}) {
  const displayName =
    profile?.displayName
    || profile?.name
    || profile?.email
    || 'ProjectPulse user';

  const jobTitle =
    profile?.jobTitle
    || profile?.title
    || 'Title not available';

  const department =
    profile?.department
    || profile?.departmentName
    || profile?.team
    || profile?.teamName
    || 'Department not available';

  const utilizationPercent = Number(
    utilization?.utilizationPercent
    ?? profile?.utilizationPercent
    ?? 0
  );

  const scheduledHours = Number(
    utilization?.scheduledHours
    ?? profile?.scheduledHours
    ?? 0
  );

  const capacityHours = Number(
    utilization?.capacityHours
    ?? utilization?.workingHours
    ?? profile?.capacityHours
    ?? profile?.workingHours
    ?? 0
  );

  const remainingHours = Math.max(
    0,
    Number(
      utilization?.remainingHours
      ?? profile?.remainingHours
      ?? capacityHours - scheduledHours
    )
  );

  return (
    <article
      className={[
        'identity-profile-card',
        compact ? 'compact' : '',
        className
      ].filter(Boolean).join(' ')}
      data-module="062"
    >
      <div className="identity-profile-heading">
        <IdentityAvatar
          profile={profile}
          size={compact ? 'small' : 'large'}
        />

        <div className="identity-profile-primary">
          <h3>{displayName}</h3>
          <p className="identity-profile-title">{jobTitle}</p>
          <p className="identity-profile-department">
            {department}
          </p>

          <IdentityPresence presence={profile?.presence} />
        </div>
      </div>

      {showCapacity ? (
        <div className="identity-profile-capacity">
          <div className="identity-utilization-value">
            <strong>{utilizationPercent}%</strong>
            <span>utilized</span>
          </div>

          <div
            className="identity-utilization-track"
            role="progressbar"
            aria-valuemin="0"
            aria-valuemax="100"
            aria-valuenow={Math.max(
              0,
              Math.min(100, utilizationPercent)
            )}
          >
            <span
              style={{
                width: `${Math.max(
                  0,
                  Math.min(100, utilizationPercent)
                )}%`
              }}
            />
          </div>

          <p>
            {scheduledHours}h scheduled / {capacityHours}h capacity
          </p>
          <p>{remainingHours}h remaining</p>
        </div>
      ) : null}
    </article>
  );
}
