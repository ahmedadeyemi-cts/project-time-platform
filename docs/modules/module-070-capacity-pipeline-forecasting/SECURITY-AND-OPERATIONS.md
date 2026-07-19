# Module 070 Security and Operations

## Authorization

- A valid effective ProjectPulse session is mandatory.
- Administrator/coordinator/executive authority receives organization scope.
- engineering/project managers and scheduling permissions receive team scope.
- other active users receive self scope.
- An `engineerUserId` outside the returned identity scope is rejected server-side.
- View-As uses the existing effective-session contract and is disclosed in the
  response; no write behavior exists to transfer.

## Data and calculation controls

- SQL is parameterized and `SELECT` only.
- No schema, migration, seed, background job, or notification is introduced.
- Raw exception details are logged server-side but never returned to the client.
- Hours are clamped to nonnegative source values; scenario supplemental hours are
  clamped to 0–10,000.
- Forecast horizons are limited to 4–52 weeks and use continuous Mondays.
- Allocated demand is deducted before team-wide pipeline weighting to reduce
  double counting with committed capacity plans.

## Operational state

Source-only and undeployed. There is no Azure, database, or Entra change and no
commit or push in this package. Activation requires current-main overlap review,
build/validator evidence, an authorized commit/push/PR, merge, deployment
authorization, and portal smoke testing.
