// Mediatheca service worker — minimum viable for PWA installability.
// Does NOT cache or intercept any requests; the network handles everything.
// The empty `fetch` listener is what Chrome's installability check looks for.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (e) => e.waitUntil(self.clients.claim()));
self.addEventListener('fetch', () => { /* no-op; let the network handle every request */ });
