if (typeof window !== 'undefined') {
  // The legacy View-As selector may remain a body-owned control, but it must not
  // insert a slot into or reparent itself inside the React-owned top bar.
  window.__projectPulseGlobalViewAsTopbarMountInstalled = true;
  window.__projectPulseReactDomOwnershipBoundary = 'view-as-body-owned';
}
