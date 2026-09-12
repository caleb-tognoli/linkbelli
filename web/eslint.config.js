import js from '@eslint/js';
import ts from 'typescript-eslint';
import svelte from 'eslint-plugin-svelte';
import globals from 'globals';
import svelteConfig from './svelte.config.js';

/**
 * There was no linter here at all — around 16 000 lines of TypeScript and Svelte with nothing
 * checking them but svelte-check's type pass. The code is consistently written because one
 * person wrote it carefully, which is not a property that survives a second contributor.
 *
 * The accessibility rules are on deliberately. They are the ones that catch the class of problem
 * this codebase has actually had: a switch with no accessible name, a heading that was really an
 * input, a control announced as "edit text".
 *
 * Deliberately no Prettier. The code here was formatted by hand and formatted well, and no
 * printWidth gets Prettier to agree with it — the closest setting still rewrites 79 of 143
 * files. Adopting it means one enormous reformatting commit, which is a decision about this
 * repository's history rather than a lint fix, so it is left alone.
 */
export default ts.config(
	js.configs.recommended,
	...ts.configs.recommended,
	...svelte.configs.recommended,

	{
		languageOptions: {
			globals: { ...globals.browser, ...globals.node }
		}
	},

	{
		files: ['**/*.svelte', '**/*.svelte.ts'],
		languageOptions: {
			parserOptions: {
				projectService: true,
				extraFileExtensions: ['.svelte'],
				parser: ts.parser,
				svelteConfig
			}
		}
	},

	{
		rules: {
			// An unused argument is usually a signature being honoured; an unused local is
			// usually a mistake. Underscore is the established way to say "on purpose".
			'@typescript-eslint/no-unused-vars': [
				'error',
				{ argsIgnorePattern: '^_', varsIgnorePattern: '^_', caughtErrors: 'none' }
			],

			// --- Three rules turned off, each because it is wrong about this codebase rather
			// --- than because it is inconvenient.

			// Wants resolve() around every href and goto(). That exists for apps served under a
			// base path; svelte.config.js sets none, so it would be 93 call sites of ceremony
			// for no behaviour. Worth turning back on the day a base path appears.
			'svelte/no-navigation-without-resolve': 'off',

			// Reports svelte-ignore comments it believes are unnecessary, but it does not run
			// the Svelte compiler and so cannot actually know. Checked: deleting one of the 40
			// it flagged immediately produced the a11y_autofocus warning it was suppressing.
			// svelte-check runs the real compiler and is the authority here.
			'svelte/no-unused-svelte-ignore': 'off',

			// Flags every `new Set`/`new Map`/`new URLSearchParams`, but all fourteen here are
			// locals built inside a function or a $derived and then assigned — which is the
			// correct Svelte 5 pattern, not a missed reactivity bug. The places that genuinely
			// hold mutable reactive collections already use SvelteSet.
			'svelte/prefer-svelte-reactivity': 'off'
		}
	},

	{
		// Generated, vendored, or not ours to have opinions about.
		ignores: ['build/', '.svelte-kit/', 'node_modules/', 'static/', 'lint-report.json']
	}
);
