/** A webhook as the API lists it. The secret is never in here. */
export interface Webhook {
	id: string;
	url: string;
	description: string | null;
	events: string[];
	status: 'Active' | 'Paused' | 'Disabled';
	consecutiveFailures: number;
	disabledReason: string | null;
	lastDeliveredAt: string | null;
	createdAt: string;
}

/** Returned once, when a webhook is made or its secret replaced. */
export interface WebhookWithSecret {
	webhook: Webhook;
	secret: string;
}

export interface WebhookDelivery {
	id: string;
	event: string;
	status: 'Pending' | 'Retrying' | 'Delivered' | 'Failed' | 'Cancelled';
	attempts: number;
	responseStatus: number | null;
	error: string | null;
	createdAt: string;
	lastAttemptAt: string | null;
	nextAttemptAt: string | null;
	deliveredAt: string | null;
}

export interface WebhookEventInfo {
	name: string;
	description: string;
}

/**
 * An address short enough for a row, keeping the part that tells two apart.
 *
 * Webhook addresses are mostly a long random path on a host somebody recognises, so the host
 * stays whole and the path is cut from the middle — the end of it is usually the token that
 * distinguishes one Home Assistant automation from another.
 */
export function shortUrl(url: string, max = 48): string {
	if (url.length <= max) return url;

	let host: string;
	let rest: string;
	try {
		const parsed = new URL(url);
		host = `${parsed.protocol}//${parsed.host}`;
		rest = `${parsed.pathname}${parsed.search}`;
	} catch {
		return `${url.slice(0, max - 1)}…`;
	}

	const room = max - host.length - 1;
	if (room < 8) return `${host}/…`;

	const head = Math.ceil(room / 2);
	const tail = room - head;
	return `${host}${rest.slice(0, head)}…${rest.slice(rest.length - tail)}`;
}

/** One line saying how a delivery went, in the words a person would use. */
export function describeDelivery(d: WebhookDelivery): string {
	const tries = d.attempts === 1 ? '1 try' : `${d.attempts} tries`;

	switch (d.status) {
		case 'Delivered':
			return d.attempts > 1 ? `Delivered after ${tries}` : 'Delivered';
		case 'Pending':
			return 'Waiting to be sent';
		case 'Retrying':
			return `Failed ${d.attempts === 1 ? 'once' : `${d.attempts} times`}, trying again`;
		case 'Failed':
			return `Gave up after ${tries}`;
		case 'Cancelled':
			return 'Not sent — the webhook was off or removed';
	}
}

/** Whether sending it again makes sense. A delivery still in progress has not finished failing. */
export function canRedeliver(d: WebhookDelivery): boolean {
	return d.status === 'Delivered' || d.status === 'Failed' || d.status === 'Cancelled';
}
