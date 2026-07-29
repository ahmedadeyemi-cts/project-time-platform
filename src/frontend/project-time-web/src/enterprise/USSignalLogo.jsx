import { usSignalLogoDataUrl } from '../assets/usSignalLogoData.js';

export default function USSignalLogo({
  className = '',
  alt = 'US Signal',
  decorative = false,
  size = 'standard'
}) {
  const normalizedSize = ['compact', 'standard', 'large'].includes(size)
    ? size
    : 'standard';

  return (
    <span
      className={`uss-logo-lockup uss-logo-lockup--${normalizedSize} ${className}`.trim()}
      data-official-us-signal-logo="true"
    >
      <img
        src={usSignalLogoDataUrl}
        alt={decorative ? '' : alt}
        aria-hidden={decorative ? 'true' : undefined}
      />
    </span>
  );
}
