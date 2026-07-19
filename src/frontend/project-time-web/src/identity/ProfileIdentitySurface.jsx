import IdentityAvatar from './IdentityAvatar.jsx';
import IdentityPresence from './IdentityPresence.jsx';
import IdentityProfileCard from './IdentityProfileCard.jsx';
import useIdentityProfile from './useIdentityProfile.js';

function fallbackProfile({
  authSession,
  currentUser
}) {
  const email =
    authSession?.username
    || authSession?.email
    || currentUser?.data?.email
    || '';

  const displayName =
    currentUser?.data?.displayName
    || authSession?.displayName
    || authSession?.name
    || email
    || 'ProjectPulse user';

  return {
    email,
    displayName,
    jobTitle:
      currentUser?.data?.jobTitle
      || currentUser?.data?.roleName
      || authSession?.roleName
      || authSession?.role
      || 'Title not available',
    department:
      currentUser?.data?.departmentName
      || currentUser?.data?.department
      || currentUser?.data?.teamName
      || 'Department not available',
    team:
      currentUser?.data?.teamName
      || '',
    role:
      currentUser?.data?.roleName
      || authSession?.roleName
      || authSession?.role
      || '',
    profilePhotoDataUrl: '',
    isMicrosoftIdentity: false,
    identitySource: 'session_fallback',
    authenticationProvider: 'backend_resolved',
    directoryProvider: 'backend_resolved',
    presence: {
      availability: 'presenceUnknown',
      activity: 'presenceUnknown',
      supported: false,
      status: 'profile_loading'
    }
  };
}

function resolveSurfaceProfile({
  profile,
  authSession,
  currentUser,
  userPreferences
}) {
  const fallback = fallbackProfile({
    authSession,
    currentUser
  });

  const source = {
    ...fallback,
    ...(profile || {})
  };

  const isMicrosoft =
    profile?.isMicrosoftIdentity === true;

  return {
    ...source,
    displayName: isMicrosoft
      ? profile?.displayName
        || fallback.displayName
      : userPreferences?.displayNameOverride
        || profile?.displayName
        || fallback.displayName,
    jobTitle: isMicrosoft
      ? profile?.jobTitle
        || fallback.jobTitle
      : userPreferences?.titleOverride
        || profile?.jobTitle
        || fallback.jobTitle,
    department:
      profile?.department
      || profile?.team
      || fallback.department,
    profilePhotoDataUrl: isMicrosoft
      ? profile?.profilePhotoDataUrl
        || fallback.profilePhotoDataUrl
      : userPreferences?.profilePhotoDataUrl
        || profile?.profilePhotoDataUrl
        || fallback.profilePhotoDataUrl,
    presence:
      profile?.presence
      || fallback.presence
  };
}

export default function ProfileIdentitySurface({
  mode = 'settings',
  authSession,
  currentUser,
  userPreferences
}) {
  const {
    profile,
    loading,
    error,
    refreshedAt
  } = useIdentityProfile({
    enabled: Boolean(
      authSession?.sessionToken
    )
  });

  const resolved = resolveSurfaceProfile({
    profile,
    authSession,
    currentUser,
    userPreferences
  });

  if (mode === 'avatar') {
    return (
      <span
        className="module062-profile-avatar-surface"
        data-module="062"
      >
        <IdentityAvatar
          profile={resolved}
          size="small"
          showPresence
        />
      </span>
    );
  }

  if (mode === 'menu') {
    return (
      <div
        className="module062-profile-menu-summary"
        data-module="062"
      >
        <IdentityAvatar
          profile={resolved}
          size="medium"
          showPresence
        />

        <div className="module062-profile-menu-copy">
          <strong>{resolved.displayName}</strong>
          <small>{resolved.email || 'Current user'}</small>
          <small>{resolved.jobTitle}</small>
          <small>{resolved.department}</small>

          <IdentityPresence
            presence={resolved.presence}
          />
        </div>
      </div>
    );
  }

  return (
    <section
      className="module062-profile-settings-surface"
      data-module="062"
      aria-label="Unified identity profile"
    >
      <IdentityProfileCard
        profile={resolved}
      />

      <div className="module062-profile-source-note">
        {loading ? (
          <p>Loading the unified identity profile…</p>
        ) : null}

        {error ? (
          <p>
            Microsoft or local profile enrichment is
            temporarily unavailable. ProjectPulse is
            displaying the authenticated session fallback.
          </p>
        ) : null}

        {!loading && !error ? (
          <p>
            {resolved.isMicrosoftIdentity
              ? 'Name, job title, department, photograph, and presence are resolved from Microsoft Graph with ProjectPulse fallback.'
              : 'This is a local ProjectPulse identity. Local profile preferences remain authoritative unless the account is later linked to Microsoft.'}
          </p>
        ) : null}

        <dl>
          <div>
            <dt>Authentication</dt>
            <dd>
              {resolved.authenticationProvider
                || 'Backend resolved'}
            </dd>
          </div>
          <div>
            <dt>Directory</dt>
            <dd>
              {resolved.directoryProvider
                || resolved.identitySource
                || 'ProjectPulse local'}
            </dd>
          </div>
          <div>
            <dt>Domain</dt>
            <dd>
              {resolved.domain
                || resolved.email?.split('@')[1]
                || 'Not available'}
            </dd>
          </div>
          <div>
            <dt>Last profile refresh</dt>
            <dd>
              {refreshedAt
                ? new Date(refreshedAt).toLocaleTimeString()
                : 'Pending'}
            </dd>
          </div>
        </dl>
      </div>
    </section>
  );
}
