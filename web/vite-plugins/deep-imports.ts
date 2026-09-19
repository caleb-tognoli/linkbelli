import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import type { Plugin } from 'vite';

/** A barrel package, and the files inside it that list what it re-exports from where. */
export interface Barrel {
	/** The name components import from, e.g. `@lucide/svelte`. */
	package: string;
	/** Barrel files, relative to the package directory, whose re-exports should be followed. */
	barrels: string[];
}

interface Target {
	file: string;
	/** What the target file exports it as: `default`, or a name. */
	exported: string;
}

/**
 * Rewrites `import { A, B } from 'some-barrel'` into one import per name, for the tests.
 *
 * Both component libraries this app uses are imported through barrels that re-export hundreds or
 * thousands of Svelte files — every icon, every bits-ui component — and a test that mounts one
 * component compiled all of them: thirty-six seconds for the first six tests, most of a minute
 * for the table. Pre-bundling the packages instead crashed inside Svelte's dev-mode helpers, so
 * this goes the other way and loads only what a component actually names.
 *
 * The map is read out of each installed package's own export statements, so it stays right
 * across upgrades — aliases and renamed exports included — instead of guessing file names from
 * export names. A name the map does not know stays on the barrel, which is slower but correct.
 */
export function deepImports(packages: Barrel[], root = process.cwd()): Plugin {
	const maps = new Map<string, Map<string, Target>>();

	for (const pkg of packages) {
		const dir = resolve(root, 'node_modules', pkg.package);
		const map = new Map<string, Target>();

		for (const barrel of pkg.barrels) {
			const path = resolve(dir, barrel);
			// Comments out first: the deprecated aliases carry doc comments with {@link …} inside
			// the export braces, and that closing brace would end the match early.
			const source = readFileSync(path, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

			for (const statement of source.matchAll(/export\s*\{([^}]*)\}\s*from\s*['"]([^'"]+)['"]/g)) {
				const file = resolve(dirname(path), statement[2]).replaceAll('\\', '/');

				for (const entry of statement[1].split(',').map((e) => e.trim()).filter(Boolean)) {
					const [exported, name = exported] = entry.split(/\s+as\s+/);
					map.set(name, { file, exported });
				}
			}
		}

		maps.set(pkg.package, map);
	}

	const pattern = new RegExp(
		`import\\s*\\{([^}]*)\\}\\s*from\\s*['"](${[...maps.keys()].map(escape).join('|')})['"];?`,
		'g'
	);

	return {
		name: 'deep-imports',
		enforce: 'pre',
		transform(code, id) {
			if (id.includes('/node_modules/') || ![...maps.keys()].some((p) => code.includes(p))) return null;

			const rewritten = code.replace(pattern, (whole: string, list: string, pkg: string) => {
				const map = maps.get(pkg)!;
				const deep: string[] = [];
				const rest: string[] = [];

				for (const entry of list.split(',').map((e) => e.trim()).filter(Boolean)) {
					const [imported, local = imported] = entry.split(/\s+as\s+/);
					const target = map.get(imported);

					if (!target) rest.push(entry);
					else if (target.exported === 'default') deep.push(`import ${local} from ${JSON.stringify(target.file)};`);
					else deep.push(`import { ${target.exported} as ${local} } from ${JSON.stringify(target.file)};`);
				}

				if (rest.length) {
					// Said, because the fallback is correct but slow — this is how three deprecated
					// icon names quietly put the whole icon set back into every table test.
					this.warn(`${id}: ${rest.join(', ')} not found in ${pkg}'s barrels; importing the whole package.`);
					deep.push(`import { ${rest.join(', ')} } from '${pkg}';`);
				}
				return deep.length ? deep.join('\n') : whole;
			});

			return rewritten === code ? null : { code: rewritten, map: null };
		}
	};
}

function escape(text: string): string {
	return text.replace(/[.*+?^${}()|[\]\\/]/g, '\\$&');
}
