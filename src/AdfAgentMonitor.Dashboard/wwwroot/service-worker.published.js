// Caution! Be sure you understand the caveats before publishing an app with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

const apiCacheName     = 'adf-api-cache-v1';
const apiPathPrefix    = '/api/';
const apiTimeoutMs     = 5000;

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
const base = '/';
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    console.info('Service worker: Install');
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    console.info('Service worker: Activate');
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    if (event.request.method !== 'GET') return fetch(event.request);

    const url = new URL(event.request.url);

    // API calls: network-first with 5-second timeout, fall back to cache.
    if (url.pathname.startsWith(apiPathPrefix)) {
        return networkFirstWithTimeout(event.request, apiCacheName, apiTimeoutMs);
    }

    // Static assets: cache-first (Blazor offline strategy).
    const shouldServeIndexHtml = event.request.mode === 'navigate'
        && !manifestUrlList.some(u => u === event.request.url);
    const request = shouldServeIndexHtml ? 'index.html' : event.request;
    const cache = await caches.open(cacheName);
    const cachedResponse = await cache.match(request);
    return cachedResponse || fetch(event.request);
}

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
        return networkPromise;
    }
}
