// Cloud-SSD no longer uses a service worker: its cache-first copy of app.js kept phones on old, broken versions.
// This file stays so that a worker installed by an earlier version updates to this one, clears its caches and
// removes itself.
self.addEventListener('install', () => self.skipWaiting());

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.map((key) => caches.delete(key))))
      .then(() => self.registration.unregister())
      .then(() => self.clients.matchAll())
      .then((clients) => clients.forEach((client) => client.navigate(client.url)))
  );
});
