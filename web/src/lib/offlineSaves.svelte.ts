import { browser } from '$app/environment';
import { api } from '$lib/api/client';
import {
	describeQueue,
	enqueue,
	isExhausted,
	outcomeFor,
	readQueue,
	recordAttempt,
	remove,
	type QueuedSave,
	type QueueStore
} from '$lib/offlineQueue';

/**
 * The queue of saves that have not reached the server, and the one thing that empties it.
 *
 * Shared rather than per-component: the indicator in the nav and the save form are looking at the
 * same queue, and two copies of it would disagree the moment either of them flushed.
 */
class OfflineSaves {
	pending = $state<QueuedSave[]>([]);
	flushing = $state(false);

	get summary(): string | null {
		return describeQueue(this.pending);
	}

	get waiting(): number {
		return this.pending.filter((q) => !isExhausted(q)).length;
	}

	get stuck(): QueuedSave[] {
		return this.pending.filter(isExhausted);
	}

	private get store(): QueueStore | null {
		if (!browser) return null;

		try {
			return localStorage;
		} catch {
			// Blocked site data. Nothing can be queued, which is worse than queueing but better
			// than throwing on every page load.
			return null;
		}
	}

	/** Reads the queue back off storage — on start, and after anything else may have changed it. */
	load() {
		const store = this.store;
		this.pending = store ? readQueue(store) : [];
	}

	/** Keeps a save for later. Returns false when there was nowhere to keep it. */
	add(save: Omit<QueuedSave, 'id' | 'queuedAt' | 'attempts'>): boolean {
		const store = this.store;
		if (!store) return false;

		this.pending = enqueue(store, save);
		return true;
	}

	drop(id: string) {
		const store = this.store;
		if (!store) return;

		this.pending = remove(store, id);
	}

	/**
	 * Tries to send everything waiting.
	 *
	 * Sequential on purpose: this runs the moment a connection comes back, which is the worst
	 * moment to open forty requests at once — and the links were saved in an order the person
	 * chose, so they may as well arrive in it.
	 */
	async flush(): Promise<void> {
		const store = this.store;
		if (!store || this.flushing) return;

		this.flushing = true;
		try {
			for (const save of readQueue(store)) {
				if (isExhausted(save)) continue;

				let status: number;
				try {
					const res = await api.post(`/playlists/${save.playlistId}/items`, {
						url: save.url,
						note: save.note
					});
					status = res.status;
				} catch {
					// Still no connection. Stop rather than marching through the rest: every one
					// of them is about to fail the same way, and a failed fetch is not a refusal.
					break;
				}

				const outcome = outcomeFor(status);
				if (outcome === 'sent') {
					this.pending = remove(store, save.id);
				} else if (outcome === 'refused') {
					this.pending = recordAttempt(store, save.id);
				} else {
					break;
				}
			}
		} finally {
			this.flushing = false;
		}
	}
}

export const offlineSaves = new OfflineSaves();

/**
 * Starts watching for a connection.
 *
 * Called once from the root layout. The `online` event is the whole point: the person who shared
 * a link underground is not going to come back and press a button, so coming back into signal has
 * to be what sends it.
 */
export function watchConnection(): () => void {
	if (!browser) return () => {};

	offlineSaves.load();

	const onOnline = () => void offlineSaves.flush();

	// Also attempted immediately: the browser may already have been online when this loaded,
	// having come back while the tab was closed.
	if (navigator.onLine) void offlineSaves.flush();

	window.addEventListener('online', onOnline);

	// Another tab may have queued or sent something. Storage events only fire in the tabs that
	// did not make the change, which is exactly the ones holding a stale copy.
	const onStorage = () => offlineSaves.load();
	window.addEventListener('storage', onStorage);

	return () => {
		window.removeEventListener('online', onOnline);
		window.removeEventListener('storage', onStorage);
	};
}
