/* Public landing only: no auth token, user identity, storage, API or query-string processing. */
(() => {
  const status = document.getElementById('host-status');
  const setTheme = theme => { document.documentElement.dataset.theme = ['dark', 'contrast'].includes(theme) ? 'dark' : 'light'; };
  setTheme(window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
  const app = window.microsoftTeams?.app;
  if (!app || window.self === window.top) {
    status.textContent = 'Open PulseApp in Teams for activity notifications, or use Open Pulse above.';
    return;
  }
  let completed = false;
  const timer = setTimeout(() => {
    if (!completed) status.textContent = 'Teams initialization is taking longer than expected. Open Pulse remains available in your browser.';
  }, 8000);
  app.initialize().then(async () => {
    completed = true; clearTimeout(timer);
    app.notifySuccess();
    status.textContent = 'PulseApp is open in Teams. Notification delivery is verified separately in Pulse.';
    app.registerOnThemeChangeHandler(setTheme);
    try { setTheme((await app.getContext()).app.theme); } catch { /* Appearance is optional, never authority. */ }
  }).catch(() => {
    completed = true; clearTimeout(timer);
    status.textContent = 'This page could not initialize in the Teams host. Open Pulse in your browser; ask your administrator to check the installed app.';
  });
})();
