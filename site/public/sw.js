// Service Worker for ReadySetSiivous Admin PWA
const CACHE_NAME = 'readysetsiivous-admin-v2';

self.addEventListener('install', (event) => {
  self.skipWaiting();
});

// Payload text is intentionally generic, even when a provider supplies extra fields.
self.addEventListener('push', (event) => {
  let payload = {};
  try { payload = event.data ? event.data.json() : {}; } catch { /* visible fallback is required */ }
  const safe = payload && typeof payload === 'object' ? payload : {};
  const url = safePushUrl(safe.url);
  const genericBodies = [
    'Your scheduled work starts soon. Open My shifts for details.',
    'Your work is still running. Open My shifts to finish or submit it.',
    'A new partner cleaning request is ready for review.',
    'New work is available for your approval. Open your account to review it.',
    'Your work schedule has changed. Open My shifts for the latest details.',
    'Notifications are ready on this device.',
  ];
  event.waitUntil(self.registration.showNotification('ReadySetSiivous', {
    body: genericBodies.includes(safe.body) ? safe.body : 'Your work schedule has an update. Open My shifts for details.',
    icon: '/brand/readyset-logo-badge.png',
    tag: typeof safe.tag === 'string' && /^[a-zA-Z0-9_-]{1,128}$/.test(safe.tag) ? safe.tag : 'readyset-work-update',
    data: { url },
  }));
});

function safePushUrl(value) {
  try {
    const url = new URL(typeof value === 'string' ? value : '/me/shifts/', self.location.origin);
    if (url.origin !== self.location.origin || !['/me/shifts/', '/admin/partner-requests/', '/account/'].includes(url.pathname)) return '/me/shifts/';
    const tenant = url.searchParams.get('tenant');
    return url.pathname + (tenant && /^[a-z0-9][a-z0-9-]{2,62}$/.test(tenant) ? '?tenant=' + encodeURIComponent(tenant) : '');
  } catch { return '/me/shifts/'; }
}
self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const url = new URL(safePushUrl(event.notification.data?.url), self.location.origin).href;
  event.waitUntil((async () => {
    const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    const existing = windows.find(client => client.url === url);
    if (existing) return existing.focus();
    return self.clients.openWindow(url);
  })());
});

self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim());
});

self.addEventListener('fetch', (event) => {
  // Network first strategy with fallback to network
  if (event.request.method !== 'GET') return;
  event.respondWith(
    fetch(event.request).catch(() => {
      return caches.match(event.request);
    })
  );
});
