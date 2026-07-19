const PRESENCE_DEFINITIONS = {
  available: {
    label: 'Available',
    cssState: 'available'
  },
  availableIdle: {
    label: 'Available — idle',
    cssState: 'available'
  },
  away: {
    label: 'Away',
    cssState: 'away'
  },
  beRightBack: {
    label: 'Be right back',
    cssState: 'away'
  },
  busy: {
    label: 'Busy',
    cssState: 'busy'
  },
  busyIdle: {
    label: 'Busy — idle',
    cssState: 'busy'
  },
  doNotDisturb: {
    label: 'Do not disturb',
    cssState: 'do-not-disturb'
  },
  focusing: {
    label: 'Focusing',
    cssState: 'do-not-disturb'
  },
  inACall: {
    label: 'In a call',
    cssState: 'busy'
  },
  inAConferenceCall: {
    label: 'In a conference call',
    cssState: 'busy'
  },
  inAMeeting: {
    label: 'In a meeting',
    cssState: 'busy'
  },
  inactive: {
    label: 'Inactive',
    cssState: 'away'
  },
  offline: {
    label: 'Offline',
    cssState: 'offline'
  },
  offWork: {
    label: 'Off work',
    cssState: 'offline'
  },
  outOfOffice: {
    label: 'Out of office',
    cssState: 'out-of-office'
  },
  presenting: {
    label: 'Presenting',
    cssState: 'do-not-disturb'
  },
  urgentInterruptionsOnly: {
    label: 'Urgent interruptions only',
    cssState: 'do-not-disturb'
  },
  presenceUnknown: {
    label: 'Status unavailable',
    cssState: 'presence-unknown'
  }
};

const TOKEN_TO_CANONICAL = new Map(
  Object.keys(PRESENCE_DEFINITIONS).map((key) => [
    key.replace(/[^a-z0-9]/gi, '').toLowerCase(),
    key
  ])
);

const DETAILED_ACTIVITY_STATES = new Set([
  'beRightBack',
  'focusing',
  'inACall',
  'inAConferenceCall',
  'inAMeeting',
  'offWork',
  'outOfOffice',
  'presenting',
  'urgentInterruptionsOnly'
]);

function compactPresenceToken(value) {
  return String(value ?? '')
    .trim()
    .replace(/[^a-z0-9]/gi, '')
    .toLowerCase();
}

export function canonicalPresenceValue(value) {
  const token = compactPresenceToken(value);

  if (!token) {
    return 'presenceUnknown';
  }

  return TOKEN_TO_CANONICAL.get(token) ?? 'presenceUnknown';
}

export function normalizePresence(presence) {
  const availability = canonicalPresenceValue(
    presence?.availability
  );

  const activity = canonicalPresenceValue(
    presence?.activity
  );

  let state = 'presenceUnknown';

  if (
    activity !== 'presenceUnknown'
    && DETAILED_ACTIVITY_STATES.has(activity)
  ) {
    state = activity;
  } else if (availability !== 'presenceUnknown') {
    state = availability;
  } else if (activity !== 'presenceUnknown') {
    state = activity;
  }

  const definition =
    PRESENCE_DEFINITIONS[state]
    ?? PRESENCE_DEFINITIONS.presenceUnknown;

  const colorSource =
    availability !== 'presenceUnknown'
      ? availability
      : state;

  const colorDefinition =
    PRESENCE_DEFINITIONS[colorSource]
    ?? definition;

  return {
    availability,
    activity,
    state,
    label: definition.label,
    cssState: colorDefinition.cssState,
    supported: presence?.supported !== false,
    retrievedAt: presence?.retrievedAt ?? null,
    rawAvailability: presence?.availability ?? null,
    rawActivity: presence?.activity ?? null
  };
}

export function presenceState(presence) {
  return normalizePresence(presence).state;
}

export function presenceLabel(presence) {
  return normalizePresence(presence).label;
}

export function presenceCssState(presence) {
  return normalizePresence(presence).cssState;
}

export function presenceIsAvailable(presence) {
  return normalizePresence(presence).availability === 'available';
}

export const MODULE_062_PRESENCE_STATES =
  Object.freeze({ ...PRESENCE_DEFINITIONS });
