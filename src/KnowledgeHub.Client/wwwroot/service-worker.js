// Development service worker — intentionally a no-op so the app never
// serves stale assets during `dotnet run`. At publish, this file is
// replaced by service-worker.published.js (real precache worker).
self.addEventListener('fetch', () => { });
