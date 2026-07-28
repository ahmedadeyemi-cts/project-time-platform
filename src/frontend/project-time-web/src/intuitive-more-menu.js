const MARKER = '__projectPulseIntuitiveMoreMenuInstalled';

if (typeof window !== 'undefined' && !window[MARKER]) {
  // The More menu is rendered by React in App.Module001.g.jsx. This module now
  // loads only its scoped stylesheet and records the ownership boundary. It must
  // never replace, prepend, append, or remove children from the React tree.
  void import('./intuitive-more-menu.css');
  window[MARKER] = true;
  window.__projectPulseReactDomOwnershipBoundary = {
    ...(window.__projectPulseReactDomOwnershipBoundary || {}),
    moreMenu: 'react-owned-v1'
  };
}
