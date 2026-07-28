const MARKER = '__projectPulseIntuitiveMoreMenuInstalled';

if (typeof window !== 'undefined' && !window[MARKER]) {
  // React renders the More menu in App.Module001.g.jsx. This runtime module loads
  // only scoped styling; it must never replace, prepend, append, or remove children.
  window[MARKER] = true;
  window.__projectPulseReactDomOwnershipBoundary = {
    ...(window.__projectPulseReactDomOwnershipBoundary || {}),
    moreMenu: 'react-owned-v1'
  };

  // Node-based contract harnesses intentionally provide a lightweight `window`
  // without a DOM or CSS loader. Import the scoped stylesheet only in a browser.
  if (typeof document !== 'undefined' && document?.head) {
    void import('./intuitive-more-menu.css');
  }
}
