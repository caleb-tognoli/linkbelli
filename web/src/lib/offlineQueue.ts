/**
 * Saves that could not reach the server, kept until they can.
 *
 * The share sheet is the main way links arrive from a phone, and a phone is exactly where there
 * is no connection. Without this, sharing a link on a train lost the link: the request failed,
 * the sheet closed, and nothing recorded that anything had been attempted.
 */
export interface QueuedSave {
	/** Local id, so a save can be dropped or retried individually. */
	id: string;
	playlistId: string;
	/** The name at the time it was queued, so the pending list reads as something, not a uuid. */
	playlistName: string;
	url: string;
	note: string | null;
	queuedAt: string;
	/** How many times sending it has failed for a reason that was not the network. */
	attempts: number;
}

export const QUEUE_KEY = 'lb_offline_saves';

/**
 * A ceiling on the queue.
 *
 * Reached only by something going wrong — a person cannot share hundreds of links by hand
 * without noticing — so it is a guard against a runaway loop filling up a phone's storage
 * rather than a limit anybody should meet.
 */
export const MAX_QUEUED = 200;

/**
 * How many times a save is retried before it is treated as one the server will never accept.
 *
 * A refusal is not a connection problem: retrying a malformed address forever would keep a
 * permanent error at the front of the queue and block everything behind it.
 */
export const MAX_ATTEMPTS = 3;

/** The slice of Storage this needs, so the logic can be tested without a browser. */
export interface QueueStore {
	getItem(key: string): string | null;
	setItem(key: string, value: string): void;
}

/**
 * Reads the queue, tolerating anything else that happens to be under the key.
 *
 * Storage is shared with every other script on the origin and survives upgrades of this code,
 * so the one thing it must not do is throw on the way in.
 */
export function readQueue(store: QueueStore): QueuedSave[] {
	let raw: string | null;
	try {
		raw = store.getItem(QUEUE_KEY);
	} catch {
		// Private windows and blocked site data both throw on access rather than returning null.
		return [];
	}

	if (!raw) return [];

	try {
		const parsed = JSON.parse(raw) as unknown;
		if (!Array.isArray(parsed)) return [];

		return parsed.filter(isQueuedSave);
	} catch {
		return [];
	}
}

function isQueuedSave(value: unknown): value is QueuedSave {
	if (typeof value !== 'object' || value === null) return false;

	const v = value as Record<string, unknown>;
	return (
		typeof v.id === 'string' &&
		typeof v.playlistId === 'string' &&
		typeof v.url === 'string' &&
		v.url.length > 0
	);
}

export function writeQueue(store: QueueStore, queue: QueuedSave[]): void {
	try {
		store.setItem(QUEUE_KEY, JSON.stringify(queue));
	} catch {
		// Out of storage, or storage denied. Losing the queue is bad, but throwing here would
		// lose the save the person is currently making as well.
	}
}

/**
 * Adds a save to the queue.
 *
 * The same address queued twice for the same playlist collapses to one entry: pressing save
 * again because nothing appeared to happen is the expected reaction, not a request for two.
 */
export function enqueue(
	store: QueueStore,
	save: Omit<QueuedSave, 'id' | 'queuedAt' | 'attempts'>,
	now: Date = new Date(),
	id: string = crypto.randomUUID()
): QueuedSave[] {
	const queue = readQueue(store);

	const already = queue.some(
		(q) => q.playlistId === save.playlistId && q.url === save.url
	);
	if (already) return queue;

	if (queue.length >= MAX_QUEUED) return queue;

	const next = [
		...queue,
		{ ...save, id, queuedAt: now.toISOString(), attempts: 0 }
	];
	writeQueue(store, next);

	return next;
}

export function remove(store: QueueStore, id: string): QueuedSave[] {
	const next = readQueue(store).filter((q) => q.id !== id);
	writeQueue(store, next);

	return next;
}

/** Records a failure that was the server's answer rather than a missing connection. */
export function recordAttempt(store: QueueStore, id: string): QueuedSave[] {
	const next = readQueue(store).map((q) =>
		q.id === id ? { ...q, attempts: q.attempts + 1 } : q
	);
	writeQueue(store, next);

	return next;
}

/** Whether this one has been refused often enough to stop asking. */
export function isExhausted(save: QueuedSave): boolean {
	return save.attempts >= MAX_ATTEMPTS;
}

/**
 * What one attempt to send a queued save concluded.
 *
 * `sent` and `refused` both mean the queue should let go of it — a link already in the playlist
 * or an address the server will never take are both settled, just differently. `offline` means
 * nothing was learned and the entry stays exactly as it was.
 */
export type FlushOutcome = 'sent' | 'refused' | 'offline';

/**
 * Statuses that are a 4xx but are not a refusal.
 *
 * The distinction the queue turns on is "will this ever succeed", not "is this a client error".
 * These three all mean try again later, and treating them as refusals is how a save that was
 * only ever early got counted three times and thrown away:
 *
 * - 408, the request timed out.
 * - 429, the rate limiter — which is exactly what flushing a backlog of twenty provokes, so the
 *   queue was reliably worst at the one moment it existed for.
 * - 401, the access cookie lapsed. Routine on a phone that has not opened the app in an hour,
 *   and the share sheet has already closed behind the person by the time it happens.
 */
const RETRYABLE = new Set([401, 408, 429]);

/** What a response status means for a queued save. */
export function outcomeFor(status: number): FlushOutcome {
	// Already in the playlist. The person's intent is satisfied, which is what the queue is for.
	if (status === 409) return 'sent';
	if (status >= 200 && status < 300) return 'sent';

	if (RETRYABLE.has(status)) return 'offline';

	// The server read it and said no. Retrying an address it will never accept is not patience.
	if (status >= 400 && status < 500) return 'refused';

	// A server that is having a bad minute is worth waiting for, like a missing connection.
	return 'offline';
}

/** What to tell someone about a queue, in the words they would use about it. */
export function describeQueue(queue: QueuedSave[]): string | null {
	if (queue.length === 0) return null;

	const stuck = queue.filter(isExhausted).length;
	const waiting = queue.length - stuck;

	if (waiting === 0) {
		return `${stuck} ${stuck === 1 ? 'link' : 'links'} could not be saved.`;
	}

	const head = `${waiting} ${waiting === 1 ? 'link' : 'links'} waiting to be saved.`;

	return stuck > 0 ? `${head} ${stuck} could not be saved at all.` : head;
}
