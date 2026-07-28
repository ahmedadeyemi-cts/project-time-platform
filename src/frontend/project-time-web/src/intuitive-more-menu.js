const MARKER = '__projectPulseIntuitiveMoreMenuInstalled';

if (typeof window !== 'undefined' && !window[MARKER]) {
  // React renders the More menu in App.Module001.g.jsx. This runtime module loads
  // only scoped styling; it must never replace, prepend, append, or remove children.
  void import('./intuitive-more-menu.css');
  window[MARKER] = true;
  window.__projectPulseReactDomOwnershipBoundary = {
    ...(window.__projectPulseReactDomOwnershipBoundary || {}),
    moreMenu: 'react-owned-v1'
  };
}
