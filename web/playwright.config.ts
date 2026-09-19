import { defineConfig, devices } from '@playwright/test';

/**
 * The end-to-end suite: a real browser against the running `docker compose` stack.
 *
 * The API has hundreds of integration tests against real Postgres and the web app had none
 * against a real API — so the seam between them, where the proxy dropped headers and the offline
 * queue gave up on the wrong statuses, was the least-tested part of the system and the one people
 * actually touch. These are a few whole journeys across it, not a second unit suite.
 *
 * Start the stack first (`docker compose up --build -d --wait` from the repo root). Locally the
 * installed Chrome is used, so nothing has to be downloaded; CI installs Playwright's Chromium.
 */
export default defineConfig({
	testDir: './e2e',
	// Journeys share one database and one rate limiter. Serial keeps them from reading as flaky
	// because two of them happened to sign up in the same second.
	workers: 1,
	fullyParallel: false,
	retries: process.env.CI ? 1 : 0,
	timeout: 60_000,
	reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
	use: {
		baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5173',
		trace: 'retain-on-failure',
		screenshot: 'only-on-failure'
	},
	projects: [
		{
			name: 'chrome',
			use: {
				...devices['Desktop Chrome'],
				channel: process.env.E2E_CHANNEL ?? (process.env.CI ? undefined : 'chrome')
			}
		}
	]
});
