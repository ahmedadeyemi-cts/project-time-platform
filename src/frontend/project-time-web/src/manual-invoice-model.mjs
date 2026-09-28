export function moneyCents(value) {
  if (!/^\d+(\.\d{1,2})?$/.test(String(value))) return null;
  const [whole, part = ''] = String(value).split('.');
  const cents = Number(whole) * 100 + Number(part.padEnd(2, '0'));
  return Number.isSafeInteger(cents) && cents <= 99999999999999 ? cents : null;
}
export function manualCharge(agreed, target, external, pulse, type) {
  const total = moneyCents(agreed), cumulative = moneyCents(type === 'final' ? agreed : target);
  const outside = moneyCents(external), prior = moneyCents(pulse);
  if ([total, cumulative, outside, prior].some(value => value === null) || total <= 0 || cumulative > total) return null;
  const charge = cumulative - outside - prior;
  return charge > 0 ? charge / 100 : null;
}
