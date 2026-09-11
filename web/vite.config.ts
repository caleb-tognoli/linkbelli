import { sveltekit } from '@sveltejs/kit/vite';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'vitest/config';

export default defineConfig({
	plugins: [tailwindcss(), sveltekit()],
	test: {
		// jsdom for the modules that touch document.cookie; everything else is pure.
		environment: 'jsdom',
		include: ['src/**/*.test.ts']
	}
});
