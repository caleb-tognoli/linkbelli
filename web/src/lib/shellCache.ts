/**
 * Whether the app has a copy of itself to open without a connection.
 *
 * - `pending` — registration has not finished, or has not been attempted.
 * - `ready` — the worker is installed; the app opens offline.
 * - `unsupported` — this browser has no service workers, or the page is not on a secure origin.
 * - `failed` — the browser has them and refused this one. `reason` says what it said.
 */
export type ShellCacheStatus = 'pending' | 'ready' | 'unsupported' | 'failed';

export interface ShellCacheOutcome {
	status: ShellCacheStatus;
	/** The reason it will not work, in words worth showing. Null when there is nothing to add. */
	reason: string | null;
}

/** What the page can see about its own environment. Passed in so this stays testable. */
export interface ShellCacheEnvironment {
	hasServiceWorker: boolean;
	protocol: string;
	hostname: string;
}

/**
 * Whether registration is worth attempting, and why not when it is not.
 *
 * Returns null when the environment allows it — the answer then depends on what the browser does,
 * which only an attempt can find out.
 *
 * The localhost exemption is not a convenience: browsers treat localhost as a secure origin
 * precisely so that development over plain http behaves like production, and without it every
 * local run would report the feature as unavailable and teach the maintainer to ignore the notice.
 */
export function shellCacheBlocked(env: ShellCacheEnvironment): ShellCacheOutcome | null {
	if (!env.hasServiceWorker) {
		return { status: 'unsupported', reason: null };
	}

	if (env.protocol !== 'https:' && env.hostname !== 'localhost') {
		return { status: 'unsupported', reason: 'Service workers need a secure connection.' };
	}

	return null;
}
