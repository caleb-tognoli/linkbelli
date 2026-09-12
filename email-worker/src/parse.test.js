import { describe, expect, it } from 'vitest';
import {
	decodePart,
	decodeWords,
	header,
	localPart,
	splitMessage,
	toIngestPayload,
	tokenFor
} from './parse.js';

describe('localPart', () => {
	it('takes the part before the at', () => {
		expect(localPart('save@in.example.com')).toBe('save');
	});

	it('ignores a plus tag, which is how people label mail', () => {
		expect(localPart('save+newsletters@in.example.com')).toBe('save');
	});

	it('keeps the case of the local part, which carries a token', () => {
		// Lowercasing would corrupt a base64url token, which is what this local part is.
		expect(localPart('AbCd@In.Example.Com')).toBe('AbCd');
	});

	it('refuses anything that is not an address', () => {
		expect(localPart('not-an-address')).toBeNull();
		expect(localPart('@example.com')).toBeNull();
		expect(localPart(null)).toBeNull();
		expect(localPart(42)).toBeNull();
	});
});

describe('tokenFor', () => {
	const token = 'C8DP6Pd2932RbFrCVfXY1_qsiQv7YDOo';

	it('reads the token straight out of the address', () => {
		expect(tokenFor(`${token}@in.example.com`)).toBe(token);
	});

	it('keeps the case, because a base64url token depends on it', () => {
		expect(tokenFor('AbCdEfGhIjKlMnOp@in.example.com')).toBe('AbCdEfGhIjKlMnOp');
	});

	it('still works through a plus tag', () => {
		expect(tokenFor(`${token}+later@in.example.com`)).toBe(token);
	});

	it('refuses anything that is not shaped like a token', () => {
		// Somebody guessing at addresses, which is not worth a request.
		expect(tokenFor('hello@in.example.com')).toBeNull();
		expect(tokenFor('a@in.example.com')).toBeNull();
		expect(tokenFor('has spaces in it@in.example.com')).toBeNull();
		expect(tokenFor(`${'x'.repeat(200)}@in.example.com`)).toBeNull();
	});

	it('refuses something that is not an address at all', () => {
		expect(tokenFor('not-an-address')).toBeNull();
		expect(tokenFor(null)).toBeNull();
	});
});

describe('decodePart', () => {
	it('leaves plain text alone', () => {
		expect(decodePart('hello https://example.com', '7bit')).toBe('hello https://example.com');
		expect(decodePart('hello', undefined)).toBe('hello');
	});

	it('decodes base64', () => {
		expect(decodePart(btoa('https://example.com/a'), 'base64')).toBe('https://example.com/a');
	});

	it('copes with base64 wrapped across lines, as mail wraps it', () => {
		const wrapped = btoa('https://example.com/a').replace(/(.{4})/g, '$1\n');

		expect(decodePart(wrapped, 'base64')).toBe('https://example.com/a');
	});

	it('decodes quoted-printable, including soft line breaks', () => {
		// A soft break is exactly how a long URL survives a 76-column limit, so getting this
		// wrong would split the one thing worth extracting.
		expect(decodePart('https://example.com/a=\r\nbc', 'quoted-printable')).toBe(
			'https://example.com/abc'
		);
		expect(decodePart('caf=C3=A9', 'quoted-printable')).toContain('caf');
	});

	it('returns the body unchanged rather than throwing on bad base64', () => {
		expect(decodePart('!!!not base64!!!', 'base64')).toBe('!!!not base64!!!');
	});
});

describe('splitMessage', () => {
	it('splits at the blank line', () => {
		const { headers, body } = splitMessage('From: a@b.com\r\nSubject: Hi\r\n\r\nBody here');

		expect(headers).toContain('Subject: Hi');
		expect(body).toBe('Body here');
	});

	it('copes with unix line endings', () => {
		const { headers, body } = splitMessage('From: a@b.com\nSubject: Hi\n\nBody here');

		expect(headers).toContain('Subject: Hi');
		expect(body).toBe('Body here');
	});

	it('treats a message with no blank line as all headers', () => {
		expect(splitMessage('From: a@b.com').body).toBe('');
	});

	it('does not throw on nothing at all', () => {
		expect(splitMessage(undefined)).toEqual({ headers: '', body: '' });
	});
});

describe('header', () => {
	const headers = 'From: Caleb <a@b.com>\r\nSubject: A long one\r\n  continued here\r\nTo: x@y.com';

	it('finds a header whatever its case', () => {
		expect(header(headers, 'from')).toBe('Caleb <a@b.com>');
		expect(header(headers, 'FROM')).toBe('Caleb <a@b.com>');
	});

	it('unfolds a header continued on the next line', () => {
		expect(header(headers, 'subject')).toBe('A long one continued here');
	});

	it('has nothing for a header that is not there', () => {
		expect(header(headers, 'reply-to')).toBeNull();
	});

	it('does not mistake a header whose name is a prefix of another', () => {
		expect(header('Content-Type: text/plain\r\nContent-Transfer-Encoding: base64', 'content-type'))
			.toBe('text/plain');
	});
});

describe('decodeWords', () => {
	it('decodes a base64 encoded-word', () => {
		const encoded = `=?utf-8?B?${btoa('Hello there')}?=`;

		expect(decodeWords(encoded)).toBe('Hello there');
	});

	it('decodes a quoted-printable encoded-word, where underscore means space', () => {
		expect(decodeWords('=?utf-8?Q?Hello_there?=')).toBe('Hello there');
	});

	it('leaves an ordinary subject alone', () => {
		expect(decodeWords('Just a subject')).toBe('Just a subject');
	});

	it('leaves an encoded-word it cannot read rather than dropping it', () => {
		const broken = '=?utf-8?B?!!!?=';

		expect(decodeWords(broken)).toBeTruthy();
	});
});

describe('toIngestPayload', () => {
	it('carries the sender, the subject and the body', () => {
		const raw = 'From: Caleb <a@b.com>\r\nSubject: Read this\r\n\r\nhttps://example.com/a';

		const payload = toIngestPayload(raw, 'a@b.com');

		expect(payload.from).toBe('a@b.com');
		expect(payload.subject).toBe('Read this');
		expect(payload.text).toContain('https://example.com/a');
	});

	it('prefers the envelope sender over the From header', () => {
		// The header is whatever the sender chose to write; the envelope is what actually
		// delivered the message.
		const raw = 'From: spoofed@elsewhere.com\r\n\r\nbody';

		expect(toIngestPayload(raw, 'real@sender.com').from).toBe('real@sender.com');
	});

	it('falls back to the From header when there is no envelope sender', () => {
		const raw = 'From: a@b.com\r\n\r\nbody';

		expect(toIngestPayload(raw, undefined).from).toBe('a@b.com');
	});

	it('decodes a body that arrived encoded', () => {
		const raw =
			'From: a@b.com\r\nContent-Transfer-Encoding: base64\r\n\r\n' +
			btoa('please read https://example.com/encoded');

		expect(toIngestPayload(raw, 'a@b.com').text).toContain('https://example.com/encoded');
	});

	it('keeps a long URL intact across a soft line break', () => {
		const raw =
			'From: a@b.com\r\nContent-Transfer-Encoding: quoted-printable\r\n\r\n' +
			'https://example.com/a-very-long-=\r\npath-that-was-wrapped';

		expect(toIngestPayload(raw, 'a@b.com').text).toContain(
			'https://example.com/a-very-long-path-that-was-wrapped'
		);
	});

	it('does not fall over on an empty message', () => {
		const payload = toIngestPayload('', 'a@b.com');

		expect(payload.from).toBe('a@b.com');
		expect(payload.subject).toBeNull();
	});
});
