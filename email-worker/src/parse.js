/**
 * Pulling the parts Linkbelli needs out of a received message.
 *
 * Kept separate from the worker so it can be tested without Cloudflare's runtime — which is
 * worth doing, because every mail client formats a message differently and this is where that
 * variety lands.
 */

/** The local part of an address: "save" out of "save@in.example.com". */
export function localPart(address) {
	if (typeof address !== 'string') return null;

	const at = address.lastIndexOf('@');
	if (at <= 0) return null;

	// Kept as sent: the local part carries a base64url token, in which case matters.
	// Plus-addressing is how people tag mail, and "token+news@..." should still reach the token.
	const local = address.slice(0, at);
	const plus = local.indexOf('+');

	return plus > 0 ? local.slice(0, plus) : local;
}

/**
 * The token an address carries, which is its local part.
 *
 * The address *is* the credential: token@in.example.com. That means no mapping table to keep in
 * step with the app — this worker needs nothing but an API URL — and, more usefully, the app can
 * show somebody their own inbox address, which it could not do if an operator chose the local
 * part separately.
 */
export function tokenFor(address) {
	const local = localPart(address);

	// Tokens are base64url. Anything else is somebody guessing at addresses, and there is no
	// reason to spend a request on it.
	return local && /^[A-Za-z0-9_-]{16,64}$/.test(local) ? local : null;
}

/**
 * The decoded text of one MIME part.
 *
 * Only the two encodings that actually appear: quoted-printable and base64. Anything else is
 * already readable, and guessing at an unknown encoding produces worse output than leaving it.
 */
export function decodePart(body, encoding) {
	const how = (encoding ?? '').toLowerCase();

	if (how === 'base64') {
		try {
			return atob(body.replace(/\s+/g, ''));
		} catch {
			return body;
		}
	}

	if (how === 'quoted-printable') {
		return (
			body
				// A trailing "=" is a soft line break, meaning the line continues.
				.replace(/=\r?\n/g, '')
				.replace(/=([0-9A-Fa-f]{2})/g, (_, hex) => String.fromCharCode(parseInt(hex, 16)))
		);
	}

	return body;
}

/**
 * Splits a raw message into the headers and the body, as text.
 *
 * A deliberately small parser. The only thing being asked of it is "where are the addresses in
 * this", which survives a great deal of imprecision — and a full MIME implementation in an email
 * worker is a large amount of code to maintain for a job the URL extractor finishes anyway.
 */
export function splitMessage(raw) {
	const text = typeof raw === 'string' ? raw : '';
	// The header block ends at the first blank line, whichever line ending the sender used.
	const breakAt = text.search(/\r?\n\r?\n/);

	if (breakAt < 0) return { headers: text, body: '' };

	const gap = text.slice(breakAt).match(/^\r?\n\r?\n/)[0].length;

	return { headers: text.slice(0, breakAt), body: text.slice(breakAt + gap) };
}

/** One header's value, unfolded. Case-insensitive, as headers are. */
export function header(headers, name) {
	const lines = (headers ?? '').split(/\r?\n/);
	const wanted = name.toLowerCase() + ':';

	for (let i = 0; i < lines.length; i++) {
		if (!lines[i].toLowerCase().startsWith(wanted)) continue;

		let value = lines[i].slice(wanted.length).trim();

		// A long header is folded across lines, continued by leading whitespace.
		while (i + 1 < lines.length && /^\s+/.test(lines[i + 1])) {
			value += ' ' + lines[++i].trim();
		}

		return value;
	}

	return null;
}

/**
 * Decodes an RFC 2047 encoded-word, which is how a subject carries anything but ASCII.
 *
 * Without this a subject in any other language arrives as "=?utf-8?B?…?=", which is both
 * unreadable and useless as a link title.
 */
export function decodeWords(value) {
	if (!value) return value;

	return value.replace(/=\?([^?]+)\?([BbQq])\?([^?]*)\?=/g, (whole, _charset, kind, text) => {
		try {
			return kind.toLowerCase() === 'b'
				? decodePart(text, 'base64')
				: decodePart(text.replace(/_/g, ' '), 'quoted-printable');
		} catch {
			return whole;
		}
	});
}

/**
 * What to send to Linkbelli: the sender, the subject, and enough body to find links in.
 *
 * The body is handed over as one string rather than split into text and HTML parts. Linkbelli
 * extracts addresses from whatever it is given, and deciding which MIME part is authoritative is
 * a distinction that changes nothing about the answer.
 */
export function toIngestPayload(raw, from) {
	const { headers, body } = splitMessage(raw);
	const encoding = header(headers, 'content-transfer-encoding');

	return {
		from: from ?? header(headers, 'from') ?? '',
		subject: decodeWords(header(headers, 'subject')) ?? null,
		text: decodePart(body, encoding),
		html: null
	};
}
