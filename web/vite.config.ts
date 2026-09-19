import { sveltekit } from '@sveltejs/kit/vite';
import tailwindcss from '@tailwindcss/vite';
import { svelteTesting } from '@testing-library/svelte/vite';
import { defineConfig } from 'vitest/config';
import { deepImports } from './vite-plugins/deep-imports';

export default defineConfig({
	plugins: [tailwindcss(), sveltekit()],
	test: {
		projects: [
			{
				extends: true,
				test: {
					name: 'unit',
					// jsdom for the modules that touch document.cookie; everything else is pure.
					environment: 'jsdom',
					include: ['src/**/*.test.ts'],
					exclude: ['src/**/*.svelte.test.ts']
				}
			},
			{
				extends: true,
				// Components are mounted, which needs Svelte's browser build — the plugin switches
				// module resolution to it (cleanup between tests is registered in the setup file).
				// Kept to its own project so the server-side tests above go on resolving exactly as
				// they did.
				plugins: [
					svelteTesting(),
					deepImports([
						{
							package: '@lucide/svelte',
							barrels: ['dist/icons/index.js', 'dist/aliases/aliases.js', 'dist/aliases/prefixed.js', 'dist/aliases/suffixed.js']
						},
						{ package: 'bits-ui', barrels: ['dist/bits/index.js'] }
					])
				],
				test: {
					name: 'components',
					environment: 'jsdom',
					include: ['src/**/*.svelte.test.ts'],
					setupFiles: ['./src/lib/testing/setup.ts']
				}
			}
		]
	}
});
