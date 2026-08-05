<script lang="ts">
	import Switch from './Switch.svelte';

	const CRON_NEVER = '0 0 30 2 *';

	function parseCron(cron: string): { count: number; unit: 'minutes' | 'hours' | 'days' } {
		const m = cron.match(/^\*\/(\d+) \* \* \* \*$/);
		if (m) return { count: +m[1], unit: 'minutes' };
		const h = cron.match(/^0 \*\/(\d+) \* \* \*$/);
		if (h) return { count: +h[1], unit: 'hours' };
		if (cron === '0 * * * *') return { count: 1, unit: 'hours' };
		const d = cron.match(/^0 0 \*\/(\d+) \* \*$/);
		if (d) return { count: +d[1], unit: 'days' };
		if (cron === '0 0 * * *') return { count: 1, unit: 'days' };
		return { count: 1, unit: 'hours' };
	}

	function buildCron(count: number, unit: 'minutes' | 'hours' | 'days'): string {
		if (unit === 'minutes') return `*/${Math.max(5, count)} * * * *`;
		if (unit === 'hours') return count === 1 ? '0 * * * *' : `0 */${count} * * *`;
		return count === 1 ? '0 0 * * *' : `0 0 */${count} * *`;
	}

	let { schedule = $bindable(), enabled = $bindable() }: { schedule: string; enabled: boolean } = $props();

	const _sched = parseCron(schedule === CRON_NEVER || !schedule ? '0 * * * *' : schedule);
	let count = $state(_sched.count);
	let unit = $state<'minutes' | 'hours' | 'days'>(_sched.unit);

	$effect(() => {
		schedule = enabled ? buildCron(count, unit) : CRON_NEVER;
	});
</script>

<div class="flex flex-wrap items-end gap-8">
	<div class="flex flex-col gap-2 text-sm">
		<span>Run every</span>
		<div class="flex items-center gap-2" class:opacity-40={!enabled}>
			<div class="inline-flex divide-x overflow-hidden rounded-md border text-sm" style="border-color: var(--color-border); --tw-divide-opacity: 1">
				<button
					type="button"
					disabled={!enabled || count <= (unit === 'minutes' ? 5 : 1)}
					onclick={() => count--}
					class="px-2.5 py-2 hover:bg-black/5 dark:hover:bg-white/10 disabled:cursor-default disabled:opacity-30"
					style="background: var(--color-bg)"
				>−</button>
				<span class="flex min-w-[2.5rem] items-center justify-center px-2 py-2 tabular-nums" style="background: var(--color-bg)">{count}</span>
				<button
					type="button"
					disabled={!enabled || count >= (unit === 'minutes' ? 59 : unit === 'hours' ? 23 : 30)}
					onclick={() => count++}
					class="px-2.5 py-2 hover:bg-black/5 dark:hover:bg-white/10 disabled:cursor-default disabled:opacity-30"
					style="background: var(--color-bg)"
				>+</button>
			</div>
			<div class="inline-flex divide-x overflow-hidden rounded-md border text-sm" style="border-color: var(--color-border)">
				{#each [['minutes', 'min'], ['hours', 'hr'], ['days', 'day']] as [val, lbl] (val)}
					<button
						type="button"
						disabled={!enabled}
						onclick={() => {
							unit = val as 'minutes' | 'hours' | 'days';
							if (val === 'minutes' && count < 5) count = 5;
							if (val === 'hours' && count > 23) count = 23;
							if (val === 'days' && count > 30) count = 30;
						}}
						class="px-3 py-2 disabled:cursor-default"
						class:font-medium={unit === val}
						style="background: {unit === val ? 'var(--color-surface)' : 'var(--color-bg)'}"
					>{lbl}</button>
				{/each}
			</div>
		</div>
	</div>

	<div class="flex flex-col gap-2 text-sm">
		<span>Enabled</span>
		<Switch bind:checked={enabled} />
	</div>
</div>
