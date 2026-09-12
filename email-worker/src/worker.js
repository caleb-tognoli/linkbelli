import { toIngestPayload, tokenFor } from './parse.js';

/**
 * A Cloudflare Email Worker that turns an emailed link into a Linkbelli save.
 *
 * Email Routing receives the message for a domain you own and runs this on it. The worker posts
 * the useful parts to /inbox/{token}, which hands them to the same webhook source a scripted push
 * would — so a mailed link gets the same filters, the same deduplication and the same quota as
 * every other way in.
 *
 * Both halves are free: Email Routing has no per-address charge, and this fits the Workers free
 * plan comfortably.
 */
export default {
	async email(message, env, ctx) {
		const token = tokenFor(message.to);

		if (!token) {
			// Rejected rather than dropped. A silent discard means somebody's link vanishes with
			// no trace anywhere, and a bounce at least tells the sender it did not arrive.
			message.setReject('No Linkbelli inbox at that address.');
			return;
		}

		let raw;
		try {
			raw = await new Response(message.raw).text();
		} catch (error) {
			// Nothing readable to forward, and a reject is the honest answer.
			message.setReject('Could not read that message.');
			return;
		}

		const payload = toIngestPayload(raw, message.from);

		let res;
		try {
			res = await fetch(`${env.LINKBELLI_API_URL}/api/v1/inbox/${encodeURIComponent(token)}`, {
				method: 'POST',
				headers: { 'content-type': 'application/json' },
				body: JSON.stringify(payload)
			});
		} catch (error) {
			// Linkbelli is down or unreachable. A temporary failure, so reject and let the
			// sending server try again rather than accepting a message we have not stored.
			message.setReject('Linkbelli could not be reached. Try again shortly.');
			return;
		}

		if (res.status === 404) {
			// The token is configured here but Linkbelli does not know it, or this sender is not
			// allowed to write to that source. Indistinguishable on purpose, at the API's end.
			message.setReject('That Linkbelli inbox does not accept mail from you.');
			return;
		}

		if (res.status === 400) {
			// A message with no addresses in it. Not an error worth retrying, and the sender
			// should know nothing was saved.
			message.setReject('There were no links in that message.');
			return;
		}

		if (!res.ok) {
			message.setReject('Linkbelli refused that message.');
		}
	}
};
