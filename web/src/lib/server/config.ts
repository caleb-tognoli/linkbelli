import { env } from '$env/dynamic/private';

/** Base URL of the Linkbelli API. Server-only — never imported into client code. */
export const API_BASE = env.API_BASE_URL ?? 'http://localhost:5180';
