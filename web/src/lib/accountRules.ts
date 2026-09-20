/**
 * What the API will accept for a username and a password, said before the form is sent.
 *
 * Sign-up used to reveal these one round trip at a time: send, "too short", send again, "needs a
 * digit", send again, "needs a symbol". The rules are the API's — UsernamePolicy for names and
 * Identity's default password options, which this instance does not override — repeated here so
 * they can be checked as they are typed. The server still has the last word.
 */

/** UsernamePolicy.MinLength and MaxLength. */
export const USERNAME_MIN = 3;
export const USERNAME_MAX = 30;

/** UsernamePolicy.AllowedCharacters, as an HTML `pattern`. */
export const USERNAME_PATTERN = '[A-Za-z0-9_\\-]{3,30}';

export const USERNAME_HINT = `${USERNAME_MIN}–${USERNAME_MAX} letters, numbers, hyphens or underscores. It is part of your public address.`;

/** Why a username would be refused, or null when it would not. Mirrors UsernamePolicy.Validate. */
export function usernameProblem(value: string): string | null {
	const name = value.trim();
	if (!name) return null;
	if (name.length < USERNAME_MIN || name.length > USERNAME_MAX) {
		return `Use between ${USERNAME_MIN} and ${USERNAME_MAX} characters.`;
	}
	if (!/^[A-Za-z0-9_-]+$/.test(name)) {
		return 'Use only letters, numbers, hyphens and underscores — not an email address.';
	}
	if (/^[-_]|[-_]$/.test(name)) return 'Do not start or end with a hyphen or underscore.';
	return null;
}

/** Identity's default PasswordOptions. */
export const PASSWORD_MIN = 6;

export interface PasswordRule {
	label: string;
	met: (password: string) => boolean;
}

export const PASSWORD_RULES: PasswordRule[] = [
	{ label: `At least ${PASSWORD_MIN} characters`, met: (p) => p.length >= PASSWORD_MIN },
	{ label: 'An upper-case letter', met: (p) => /[A-Z]/.test(p) },
	{ label: 'A lower-case letter', met: (p) => /[a-z]/.test(p) },
	{ label: 'A number', met: (p) => /[0-9]/.test(p) },
	{ label: 'A symbol, like ! or #', met: (p) => /[^A-Za-z0-9]/.test(p) }
];

export function passwordAcceptable(password: string): boolean {
	return PASSWORD_RULES.every((rule) => rule.met(password));
}
