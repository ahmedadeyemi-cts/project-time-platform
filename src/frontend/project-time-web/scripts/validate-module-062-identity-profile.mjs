import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const root = process.cwd();

const read = (...parts) =>
  fs.readFileSync(
    path.join(root, ...parts),
    'utf8'
  );

const calendar = read(
  'src',
  'CalendarCapacityCenter.jsx'
);

const presence = read(
  'src',
  'identity',
  'presence.js'
);

const avatar = read(
  'src',
  'identity',
  'IdentityAvatar.jsx'
);

const profileCard = read(
  'src',
  'identity',
  'IdentityProfileCard.jsx'
);

const hook = read(
  'src',
  'identity',
  'useIdentityProfile.js'
);

const readme = read(
  '..',
  '..',
  '..',
  'docs',
  'modules',
  'module-062-unified-identity-profile',
  'README.md'
);

const identityBackend = read(
  '..',
  '..',
  '..',
  'src',
  'backend',
  'ProjectTime.Api',
  'Modules',
  'IdentityProfileModule.cs'
);

const program = read(
  '..',
  '..',
  '..',
  'src',
  'backend',
  'ProjectTime.Api',
  'Program.cs'
);

const sessionDrawer = read(
  'src',
  'SessionIntelligenceDrawer.jsx'
);

const app = read(
  'src',
  'App.jsx'
);

const profileSurface = read(
  'src',
  'identity',
  'ProfileIdentitySurface.jsx'
);

const presenceRuntime = await import(
  pathToFileURL(
    path.join(
      root,
      'src',
      'identity',
      'presence.js'
    )
  ).href
);

const availableRuntimePresence =
  presenceRuntime.normalizePresence({
    availability: 'Available',
    activity: 'Available'
  });

const meetingRuntimePresence =
  presenceRuntime.normalizePresence({
    availability: 'Busy',
    activity: 'InAMeeting'
  });

const dndRuntimePresence =
  presenceRuntime.normalizePresence({
    availability: 'DoNotDisturb',
    activity: 'DoNotDisturb'
  });

const assertions = [];

function assert(name, condition, detail = '') {
  assertions.push({
    name,
    condition,
    detail
  });

  console.log(
    `${name}=${condition ? 'PASSED' : 'FAILED'}`
    + (
      detail
        ? ` — ${detail}`
        : ''
    )
  );
}

assert(
  'MODULE_062_SHARED_PRESENCE_NORMALIZER',
  presence.includes(
    'export function normalizePresence'
  )
);

assert(
  'MODULE_062_CASE_INSENSITIVE_PRESENCE',
  presence.includes(
    ".toLowerCase()"
  )
  && presence.includes(
    'canonicalPresenceValue'
  )
);

assert(
  'MODULE_062_AVAILABLE_GRAPH_VALUE_SUPPORTED',
  presence.includes(
    "available:"
  )
  && presence.includes(
    "label: 'Available'"
  )
);

assert(
  'MODULE_062_DETAILED_ACTIVITY_SUPPORTED',
  [
    'inACall',
    'inAConferenceCall',
    'inAMeeting',
    'outOfOffice',
    'presenting'
  ].every((value) =>
    presence.includes(value)
  )
);

assert(
  'MODULE_062_CALENDAR_USES_SHARED_NORMALIZER',
  calendar.includes(
    "from './identity/presence.js';"
  )
  && !calendar.includes(
    'const PRESENCE_LABELS'
  )
);

assert(
  'MODULE_062_SHARED_AVATAR',
  avatar.includes(
    'IdentityPresence'
  )
  && avatar.includes(
    'profilePhotoDataUrl'
  )
);

assert(
  'MODULE_062_SHARED_PROFILE_CARD',
  profileCard.includes(
    'jobTitle'
  )
  && profileCard.includes(
    'department'
  )
  && profileCard.includes(
    'IdentityPresence'
  )
);

assert(
  'MODULE_062_PROFILE_ENDPOINT_HOOK',
  hook.includes(
    "'/api/identity/profile'"
  )
);

assert(
  'MODULE_062_DOMAIN_AUTHORITY_DOCUMENTED',
  [
    'onenecklab.com',
    'ussignal.com',
    'ussignal.local',
    'ussignal.cloud'
  ].every((domain) =>
    readme.includes(domain)
  )
);

assert(
  'MODULE_062_BACKEND_PROFILE_ENDPOINT',
  identityBackend.includes(
    '"/api/identity/profile"'
  )
  && identityBackend.includes(
    'identity_profile_loaded'
  )
);

assert(
  'MODULE_062_EFFECTIVE_SESSION_IDENTITY',
  identityBackend.includes(
    'ProjectPulseEffectiveUserId'
  )
  && identityBackend.includes(
    'ProjectPulseSessionUserId'
  )
);

assert(
  'MODULE_062_DOMAIN_PROVIDER_ROUTING',
  [
    'onenecklab.com',
    'ussignal.com',
    'ussignal.local',
    'ussignal.cloud'
  ].every((domain) =>
    identityBackend.includes(domain)
  )
);

assert(
  'MODULE_062_MICROSOFT_GRAPH_PROFILE',
  identityBackend.includes(
    '$select=id,displayName,mail,'
  )
  && identityBackend.includes(
    'jobTitle,department'
  )
);

assert(
  'MODULE_062_MICROSOFT_GRAPH_PRESENCE',
  identityBackend.includes(
    '"/presence"'
  )
  && identityBackend.includes(
    'graph_presence_loaded'
  )
);

assert(
  'MODULE_062_LOCAL_GRAPH_FALLBACK',
  identityBackend.includes(
    'graph_temporarily_unavailable'
  )
  && identityBackend.includes(
    'projectpulse_local'
  )
);

assert(
  'MODULE_062_ENDPOINT_REGISTERED',
  program.includes(
    'IdentityProfileModule'
    + '.MapIdentityProfileEndpoints(app);'
  )
);

assert(
  'MODULE_059_CONSUMES_MODULE_062',
  sessionDrawer.includes(
    'useIdentityProfile'
  )
  && sessionDrawer.includes(
    'IdentityPresence'
  )
  && sessionDrawer.includes(
    'identityProfile?.jobTitle'
  )
  && sessionDrawer.includes(
    'identityProfile?.department'
  )
);

assert(
  'MODULE_062_AVAILABLE_RUNTIME_LABEL',
  availableRuntimePresence.label === 'Available'
  && availableRuntimePresence.cssState === 'available',
  `${availableRuntimePresence.label} / ${availableRuntimePresence.cssState}`
);

assert(
  'MODULE_062_MEETING_RUNTIME_LABEL',
  meetingRuntimePresence.label === 'In a meeting'
  && meetingRuntimePresence.cssState === 'busy',
  `${meetingRuntimePresence.label} / ${meetingRuntimePresence.cssState}`
);

assert(
  'MODULE_062_DND_RUNTIME_LABEL',
  dndRuntimePresence.label === 'Do not disturb'
  && dndRuntimePresence.cssState === 'do-not-disturb',
  `${dndRuntimePresence.label} / ${dndRuntimePresence.cssState}`
);

assert(
  'MODULE_062_PROFILE_SURFACE_COMPONENT',
  profileSurface.includes(
    'useIdentityProfile'
  )
  && profileSurface.includes(
    'IdentityProfileCard'
  )
  && profileSurface.includes(
    'IdentityAvatar'
  )
  && profileSurface.includes(
    'IdentityPresence'
  )
);

assert(
  'MODULE_062_PROFILE_MODAL_CONSUMER',
  app.includes(
    "from './identity/ProfileIdentitySurface.jsx';"
  )
  && app.includes(
    'mode="settings"'
  )
);

assert(
  'MODULE_062_PROFILE_MENU_CONSUMER',
  app.includes(
    'mode="avatar"'
  )
  && app.includes(
    'mode="menu"'
  )
);

assert(
  'MODULE_062_MODE_AWARE_TENANT_ROUTING',
  identityBackend.includes(
    'PROJECTPULSE_ENTRA_MODE'
  )
  && identityBackend.includes(
    'allowGenericTest'
  )
  && identityBackend.includes(
    'allowGenericProduction'
  )
);

const failed = assertions.filter(
  (assertion) => !assertion.condition
);

if (failed.length) {
  console.error(
    '\nModule 062 Phase 1 contract failed.'
  );

  for (const failure of failed) {
    console.error(
      `- ${failure.name}: ${failure.detail}`
    );
  }

  process.exit(1);
}

console.log(
  '\nMODULE_062_PHASE_1_CONTRACT=PASSED'
);
