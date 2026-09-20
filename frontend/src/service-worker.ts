/// <reference types="@sveltejs/kit" />
/// <reference no-default-lib="true" />
/// <reference lib="esnext" />
/// <reference lib="webworker" />

// Warehouses have wifi dead zones - behind racking, in cold storage, in the
// far corner. This is what turns walking into one from "the app is a blank
// white screen" into "the app still opens, showing what it last knew". It
// caches the shell only - the built JS/CSS/HTML and static files - never API
// responses: those come from a different origin (PUBLIC_API_BASE_URL) that
// this worker never intercepts, so a stale cached inventory count is never a
// risk this introduces. Writes made while offline are a separate concern
// entirely, handled by $lib/offlineQueue.svelte.ts in the app itself, not
// here - a service worker queuing arbitrary POST bodies for silent replay is
// exactly the kind of thing that would make the RowVersion-conflict problem
// this task calls out worse, not better, since it would replay with no
// chance for the app to show the user what happened.

import { build, files, version } from '$service-worker';

const self_ = self as unknown as ServiceWorkerGlobalScope;

const CACHE_NAME = `inventria-shell-${version}`;

// Everything the build produced (hashed JS/CSS chunks) plus everything under
// static/ (favicon, robots.txt, ...) - not `prerendered`, since this app has
// no prerendered routes to add.
const ASSETS = [...build, ...files];

self_.addEventListener('install', (event) => {
	event.waitUntil(
		(async () => {
			const cache = await caches.open(CACHE_NAME);
			await cache.addAll(ASSETS);
		})()
	);

	// Don't wait for every open tab to close before this version takes over -
	// a warehouse terminal left on for a shift shouldn't need a manual reload
	// to pick up the assets it will actually need once it goes offline.
	self_.skipWaiting();
});

self_.addEventListener('activate', (event) => {
	event.waitUntil(
		(async () => {
			const keys = await caches.keys();
			await Promise.all(keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key)));
			await self_.clients.claim();
		})()
	);
});

self_.addEventListener('fetch', (event) => {
	const { request } = event;

	// Never touch a write - queueing one is the app's job (see
	// offlineQueue.svelte.ts), and this worker answering a POST from cache
	// would be nonsensical regardless.
	if (request.method !== 'GET') return;

	const url = new URL(request.url);

	// Only the app's own origin. The API lives on a different origin
	// (PUBLIC_API_BASE_URL) and is left entirely alone - a request to it that
	// fails offline should fail exactly the way `fetch` normally fails, which
	// is what the app's own offline handling is built to catch.
	if (url.origin !== self_.location.origin) return;

	event.respondWith(
		(async () => {
			const cache = await caches.open(CACHE_NAME);

			// A precached build asset is immutable (content-hashed filename), so
			// serving it from cache first is correct, not just faster.
			if (ASSETS.includes(url.pathname)) {
				const cached = await cache.match(url.pathname);
				if (cached) return cached;
			}

			// Everything else - page navigations included - network first, so a
			// connected user always sees current data and layout, falling back to
			// whatever was last cached only once the network genuinely fails.
			try {
				const response = await fetch(request);

				// Only a real, complete response is worth keeping - an opaque
				// cross-origin response or a server error would poison the cache
				// with something worse than just falling through to it later.
				if (response.status === 200) {
					cache.put(request, response.clone());
				}

				return response;
			} catch (err) {
				const cached = await cache.match(request);
				if (cached) return cached;
				throw err;
			}
		})()
	);
});
