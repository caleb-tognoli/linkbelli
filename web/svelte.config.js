import adapter from '@sveltejs/adapter-node';
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

/** @type {import('@sveltejs/kit').Config} */
const config = {
	preprocess: vitePreprocess(),
	compilerOptions: {
		// We intentionally seed local $state from props for editable copies of server data
		// (paired with {#key} remounts where it matters). Silence only that advisory.
		warningFilter: (w) => w.code !== 'state_referenced_locally'
	},
	kit: {
		adapter: adapter()
	}
};

export default config;
