import { browser, dev } from '$app/environment';
import { shellCacheBlocked, type ShellCacheStatus } from '$lib/shellCache';

/**
 * What the service worker is actually doing.
 *
 * Registration used to be attempted and its failure discarded, on the reasoning that "a failed
 * registration costs the app nothing it had before". That is wrong: it costs the app the feature
 * it advertises — the README and web/docs/offline.md both promise the app opens with no
 * connection — and it costs the maintainer any chance of noticing, because a capability that
 * fails in silence looks exactly like one that works.
 *
 * What it does not cost is the save queue. That is localStorage, it is what keeps a link the
 * server never received, and it works whether or not this ever installs. The two are described
 * separately because conflating them would either overstate the damage or hide it.
 */
class ShellCache {
	status = $state<ShellCacheStatus>('pending');

	/** The browser's own words, when it refused. Null otherwise. */
	reason = $state<string | null>(null);

	/** True once it is known that this browser will not keep a copy of the app. */
	get unavailable(): boolean {
		return this.status === 'unsupported' || this.status === 'failed';
	}

	/**
	 * Registers the worker that makes the app open with no connection.
	 *
	 * Done here rather than left to the framework: nothing in the built output registered it, and
	 * a service worker that is compiled but never installed is worse than not having one — it
	 * looks like offline support and provides none.
	 */
	async register(): Promise<void> {
		if (!browser) return;

		const blocked = shellCacheBlocked({
			hasServiceWorker: 'serviceWorker' in navigator,
			protocol: location.protocol,
			hostname: location.hostname
		});

		if (blocked) {
			this.status = blocked.status;
			this.reason = blocked.reason;
			return;
		}

		try {
			await navigator.serviceWorker.register('/service-worker.js', {
				type: dev ? 'module' : 'classic'
			});
			this.status = 'ready';
		} catch (e) {
			this.status = 'failed';
			this.reason = e instanceof Error ? e.message : String(e);

			// Said out loud. Some browsers refuse for reasons of their own — an embedded webview,
			// a private window, an enterprise policy — and the only way anybody finds out which
			// is if the app says which.
			console.warn(
				'Linkbelli: could not install the offline shell, so the app will not open ' +
					'without a connection. Saving a link from an open tab still works.',
				e
			);
		}
	}
}

export const shellCache = new ShellCache();

/** Kept as a function so the root layout reads the same as it did. */
export function registerServiceWorker(): void {
	void shellCache.register();
}
