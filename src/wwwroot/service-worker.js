// Service Worker for bussin PWA
// This enables offline capability and installability

// Increment this version number when you want to force update all PWA installations
const CACHE_VERSION = 'v1';
const CACHE_NAME = `slimsbe-${CACHE_VERSION}`;

// Only cache truly static assets - don't cache Blazor framework files
const STATIC_CACHE_URLS = [
    './manifest.json',
    './favicon.png',
    './css/app.css',
    './css/custom-theme.css',
    './js/storage.js',
    './js/servicebus-api.js'
];

// Install event - cache static assets
self.addEventListener('install', event => {
    console.log('[Service Worker] Installing...');
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then(cache => {
                console.log('[Service Worker] Caching static assets');
                return cache.addAll(STATIC_CACHE_URLS.map(url => new Request(url, { cache: 'reload' })));
            })
            .catch(err => {
                console.error('[Service Worker] Cache failed:', err);
            })
    );
    self.skipWaiting();
});

// Activate event - clean up old caches
self.addEventListener('activate', event => {
    console.log('[Service Worker] Activating...');
    event.waitUntil(
        caches.keys().then(cacheNames => {
            return Promise.all(
                cacheNames.map(cacheName => {
                    if (cacheName !== CACHE_NAME) {
                        console.log('[Service Worker] Deleting old cache:', cacheName);
                        return caches.delete(cacheName);
                    }
                })
            );
        })
    );
    self.clients.claim();
});

// Fetch event - network first, fallback to cache for specific resources only
self.addEventListener('fetch', event => {
    // Skip non-GET requests
    if (event.request.method !== 'GET') {
        return;
    }

    // Skip Azure Service Bus requests (always need fresh data)
    if (event.request.url.includes('.servicebus.windows.net')) {
        return;
    }

    // Skip authentication requests - never cache these
    if (event.request.url.includes('login.microsoftonline.com') || 
        event.request.url.includes('authentication/')) {
        return;
    }

    // NEVER cache Blazor framework files - they change with every build
    if (event.request.url.includes('/_framework/') || 
        event.request.url.includes('/index.html') ||
        event.request.url.includes('blazor.webassembly.js')) {
        // Always fetch fresh from network
        return;
    }

    // Only cache specific static assets (CSS, JS helpers, images)
    const shouldCache = event.request.url.includes('/css/') ||
                       event.request.url.includes('/js/storage.js') ||
                       event.request.url.includes('/js/servicebus-api.js') ||
                       event.request.url.includes('favicon.png') ||
                       event.request.url.includes('manifest.json');

    if (!shouldCache) {
        // Don't cache, just fetch
        return;
    }

    // Cache strategy: Network first, fallback to cache
    event.respondWith(
        fetch(event.request)
            .then(response => {
                // Clone the response before caching
                const responseToCache = response.clone();
                
                // Cache successful responses
                if (response.status === 200) {
                    caches.open(CACHE_NAME).then(cache => {
                        cache.put(event.request, responseToCache);
                    });
                }
                
                return response;
            })
            .catch(() => {
                // Network failed, try cache
                return caches.match(event.request);
            })
    );
});

// Handle messages from clients
self.addEventListener('message', event => {
    if (event.data && event.data.type === 'SKIP_WAITING') {
        self.skipWaiting();
    }
});
