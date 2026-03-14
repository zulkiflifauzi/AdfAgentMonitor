// Development service worker.
//
// Static assets (Blazor framework files, CSS, JS) are always fetched from the
// network so that hot-reload and file-watcher changes are immediately visible.
//
// API calls use a network-first strategy with a 5-second timeout: if the
// backend doesn't respond in time the last cached response is returned so the
// dashboard degrades gracefully rather than showing a hard error.

const API_CACHE_NAME  = 'adf-api-dev-v1';
const API_PATH_PREFIX = '/api/';
const TIMEOUT_MS      = 5000;

self.addEventListener('install',  () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

self.addEventListener('fetch', event => {
    const { request } = event;
    if (request.method !== 'GET') return;                      // let mutations pass through

    const url = new URL(request.url);
    if (url.pathname.startsWith(API_PATH_PREFIX)) {
        event.respondWith(networkFirstWithTimeout(request, API_CACHE_NAME, TIMEOUT_MS));
    }
    // Everything else (Blazor framework, CSS, fonts…): network only in dev.
});

/**
 * Tries the network first; falls back to the named cache if the network is
 * slow (> timeoutMs) or unavailable. Caches every successful network response.
 */
async function networkFirstWithTimeout(request, cacheName, timeoutMs) {
    const cache = await caches.open(cacheName);

    const networkPromise = fetch(request.clone()).then(response => {
        if (response.ok) cache.put(request, response.clone());
        return response;
    });

    const timeoutPromise = new Promise((_, reject) =>
        setTimeout(() => reject(new Error('Network timeout')), timeoutMs));

    try {
        return await Promise.race([networkPromise, timeoutPromise]);
    } catch {
        const cached = await cache.match(request);
        if (cached) return cached;
        // No cached response — re-throw so the browser shows its own error.
        return networkPromise;
    }
}
