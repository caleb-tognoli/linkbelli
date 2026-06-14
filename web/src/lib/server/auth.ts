import { dev } from '$app/environment';
import { env } from '$env/dynamic/private';
import type { Cookies } from '@sveltejs/kit';

export const ACCESS_COOKIE = 'lb_access';
export const REFRESH_COOKIE = 'lb_refresh';

// Secure cookies require HTTPS, so the browser won't send them over plain http. Default to
// secure in production, but allow COOKIE_SECURE=false for local HTTP runs (e.g. docker-compose).
const SECURE = env.COOKIE_SECURE ? env.COOKIE_SECURE === 'true' : !dev;

/** Identity bearer-token login/refresh response shape. */
export interface TokenResponse {
	tokenType: string;
	accessToken: string;
	expiresIn: number;
	refreshToken: string;
}

const REFRESH_MAX_AGE = 60 * 60 * 24 * 14; // 14 days

/** Persist tokens in httpOnly cookies (never exposed to client JS). */
export function setTokens(cookies: Cookies, tokens: TokenResponse): void {
	const base = { path: '/', httpOnly: true, sameSite: 'lax', secure: SECURE } as const;
	cookies.set(ACCESS_COOKIE, tokens.accessToken, { ...base, maxAge: tokens.expiresIn });
	cookies.set(REFRESH_COOKIE, tokens.refreshToken, { ...base, maxAge: REFRESH_MAX_AGE });
}

export function clearTokens(cookies: Cookies): void {
	cookies.delete(ACCESS_COOKIE, { path: '/' });
	cookies.delete(REFRESH_COOKIE, { path: '/' });
}
