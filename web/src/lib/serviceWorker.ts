import { browser, dev } from '$app/environment';

/**
 * Registers the service worker that makes the app open with no connection.
 *
 * Done here rather than left to the framework: nothing in the built output registered it, and a
 * service worker that is compiled but never installed is worse than not having one — it looks
 * like offline support and provides none.
 */
export function registerServiceWorker(): void {
	if (!browser || !('serviceWorker' in navigator)) return;

	// Only over HTTPS or on localhost; anywhere else the browser refuses and logs an error that
	// tells the reader nothing about why.
	if (location.protocol !== 'https:' && location.hostname !== 'localhost') return;

	void navigator.serviceWorker
		.register('/service-worker.js', { type: dev ? 'module' : 'classic' })
		.catch(() => {
			// A failed registration costs the app nothing it had before, so there is nothing
			// useful to tell anyone about it.
		});
}
