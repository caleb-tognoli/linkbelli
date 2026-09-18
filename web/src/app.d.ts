// See https://svelte.dev/docs/kit/types#app.d.ts

declare global {
	namespace App {
		interface Locals {
			/** Whether the request carries a usable access token (after any refresh). */
			authenticated: boolean;
			/**
			 * Server-side API caller: prefixes the API base, injects the bearer token from the
			 * httpOnly cookie, and transparently refreshes + retries once on a 401.
			 */
			api: (path: string, init?: RequestInit) => Promise<Response>;
			/**
			 * The trace id of this browser request, sent to the API as a traceparent and echoed
			 * back as X-Request-Id — the same string in the browser, this server's logs and the
			 * API's.
			 */
			requestId: string;
		}
		// interface Error {}
		// interface PageData {}
		// interface PageState {}
		// interface Platform {}
	}
}

export {};
