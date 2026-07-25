export const PROJECTPULSE_ROLLING_YEAR_WINDOW = Object.freeze({
  previousYears: 3,
  currentYears: 1,
  futureYears: 6,
  totalYears: 10
});

/**
 * Returns a rolling ten-year window containing three prior years,
 * the current year, and six future years.
 *
 * Example for 2026: 2023 through 2032.
 */
export function getRollingYearOptions(referenceYear = new Date().getFullYear()) {
  const currentYear = Number(referenceYear);

  if (!Number.isInteger(currentYear) || currentYear < 1) {
    throw new TypeError('referenceYear must be a positive integer year.');
  }

  const firstYear = currentYear - PROJECTPULSE_ROLLING_YEAR_WINDOW.previousYears;

  return Array.from(
    { length: PROJECTPULSE_ROLLING_YEAR_WINDOW.totalYears },
    (_, index) => firstYear + index
  );
}
