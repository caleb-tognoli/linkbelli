import { beforeEach, describe, expect, it } from 'vitest';
import {
	describeQueue,
	enqueue,
	isExhausted,
	MAX_ATTEMPTS,
	MAX_QUEUED,
	outcomeFor,
	QUEUE_KEY,
	readQueue,
	recordAttempt,
	remove,
	writeQueue,
	type QueueStore,
	type QueuedSave
} from './offlineQueue';

/** A Storage stand-in, so the queue can be tested without a browser. */
function memoryStore(initial: string | null = null): QueueStore & { raw(): string | null } {
	let value = initial;

	return {
		getItem: () => value,
		setItem: (_k, v) => {
			value = v;
		},
		raw: () => value
	};
}

/** A store that throws the way a private window or blocked site data does. */
const hostileStore: QueueStore = {
	getItem: () => {
		throw new DOMException('denied');
	},
	setItem: () => {
		throw new DOMException('denied');
	}
};

const save = (over: Partial<QueuedSave> = {}) => ({
	playlistId: 'p1',
	playlistName: 'Reading',
	url: 'https://example.com/a',
	note: null,
	...over
});

describe('readQueue', () => {
	it('is empty when nothing has been queued', () => {
		expect(readQueue(memoryStore())).toEqual([]);
	});

	it('does not throw when storage itself refuses', () => {
		// A private window throws on access rather than returning null, and losing the queue is
		// not a reason to break the page that reads it.
		expect(readQueue(hostileStore)).toEqual([]);
	});

	it('ignores anything that is not a queue', () => {
		expect(readQueue(memoryStore('not json'))).toEqual([]);
		expect(readQueue(memoryStore('{"a":1}'))).toEqual([]);
		expect(readQueue(memoryStore('null'))).toEqual([]);
	});

	it('drops entries that are missing what a save needs', () => {
		const mixed = JSON.stringify([
			{ id: 'a', playlistId: 'p1', url: 'https://example.com/1', attempts: 0 },
			{ id: 'b', playlistId: 'p1' },
			{ id: 'c', playlistId: 'p1', url: '' }
		]);

		expect(readQueue(memoryStore(mixed)).map((q) => q.id)).toEqual(['a']);
	});
});

describe('enqueue', () => {
	let store: ReturnType<typeof memoryStore>;

	beforeEach(() => {
		store = memoryStore();
	});

	it('keeps the save with everything needed to send it later', () => {
		const queue = enqueue(store, save(), new Date('2026-09-12T10:00:00Z'), 'id-1');

		expect(queue).toEqual([
			{
				id: 'id-1',
				playlistId: 'p1',
				playlistName: 'Reading',
				url: 'https://example.com/a',
				note: null,
				queuedAt: '2026-09-12T10:00:00.000Z',
				attempts: 0
			}
		]);
	});

	it('survives a round trip through storage', () => {
		enqueue(store, save(), new Date(), 'id-1');

		expect(readQueue(store)).toHaveLength(1);
	});

	it('collapses the same link queued twice', () => {
		// Pressing save again because nothing appeared to happen is the expected reaction.
		enqueue(store, save(), new Date(), 'id-1');
		const queue = enqueue(store, save(), new Date(), 'id-2');

		expect(queue).toHaveLength(1);
		expect(queue[0].id).toBe('id-1');
	});

	it('treats the same link into a different playlist as a different save', () => {
		enqueue(store, save(), new Date(), 'id-1');
		const queue = enqueue(store, save({ playlistId: 'p2' }), new Date(), 'id-2');

		expect(queue).toHaveLength(2);
	});

	it('stops growing at the ceiling', () => {
		writeQueue(
			store,
			Array.from({ length: MAX_QUEUED }, (_, i) => ({
				id: `id-${i}`,
				playlistId: 'p1',
				playlistName: 'Reading',
				url: `https://example.com/${i}`,
				note: null,
				queuedAt: '2026-09-12T10:00:00.000Z',
				attempts: 0
			}))
		);

		expect(enqueue(store, save({ url: 'https://example.com/new' }))).toHaveLength(MAX_QUEUED);
	});

	it('does not throw when storage cannot be written', () => {
		// The save in front of the person matters more than recording it for later.
		expect(() => enqueue(hostileStore, save())).not.toThrow();
	});
});

describe('remove and recordAttempt', () => {
	it('lets go of one save', () => {
		const store = memoryStore();
		enqueue(store, save(), new Date(), 'a');
		enqueue(store, save({ url: 'https://example.com/b' }), new Date(), 'b');

		expect(remove(store, 'a').map((q) => q.id)).toEqual(['b']);
	});

	it('counts refusals against only the save that was refused', () => {
		const store = memoryStore();
		enqueue(store, save(), new Date(), 'a');
		enqueue(store, save({ url: 'https://example.com/b' }), new Date(), 'b');

		const queue = recordAttempt(store, 'a');

		expect(queue.find((q) => q.id === 'a')!.attempts).toBe(1);
		expect(queue.find((q) => q.id === 'b')!.attempts).toBe(0);
	});

	it('gives up after enough refusals', () => {
		const store = memoryStore();
		enqueue(store, save(), new Date(), 'a');
		for (let i = 0; i < MAX_ATTEMPTS; i++) recordAttempt(store, 'a');

		expect(isExhausted(readQueue(store)[0])).toBe(true);
	});
});

describe('outcomeFor', () => {
	it('counts a save that landed', () => {
		expect(outcomeFor(200)).toBe('sent');
		expect(outcomeFor(201)).toBe('sent');
	});

	it('counts a link already there as done, because it is', () => {
		expect(outcomeFor(409)).toBe('sent');
	});

	it('stops asking when the server read it and said no', () => {
		expect(outcomeFor(400)).toBe('refused');
		expect(outcomeFor(404)).toBe('refused');
	});

	it('waits out a server having a bad minute', () => {
		// Indistinguishable from a missing connection as far as the queue is concerned.
		expect(outcomeFor(500)).toBe('offline');
		expect(outcomeFor(503)).toBe('offline');
	});
});

describe('describeQueue', () => {
	const queued = (attempts: number, i = 0): QueuedSave => ({
		id: `id-${i}`,
		playlistId: 'p1',
		playlistName: 'Reading',
		url: `https://example.com/${i}`,
		note: null,
		queuedAt: '2026-09-12T10:00:00.000Z',
		attempts
	});

	it('says nothing when there is nothing to say', () => {
		expect(describeQueue([])).toBeNull();
	});

	it('counts what is waiting', () => {
		expect(describeQueue([queued(0)])).toBe('1 link waiting to be saved.');
		expect(describeQueue([queued(0, 1), queued(0, 2)])).toBe('2 links waiting to be saved.');
	});

	it('separates what is waiting from what has given up', () => {
		expect(describeQueue([queued(0, 1), queued(MAX_ATTEMPTS, 2)])).toBe(
			'1 link waiting to be saved. 1 could not be saved at all.'
		);
	});

	it('does not claim anything is waiting when nothing is', () => {
		expect(describeQueue([queued(MAX_ATTEMPTS)])).toBe('1 link could not be saved.');
	});
});
