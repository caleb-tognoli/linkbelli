<script lang="ts">
	import { average, toneColor, verdict } from '$lib/sourceHealth';
	import type { SourceHealth } from '$lib/types';

	let { health }: { health: SourceHealth } = $props();

	const summary = $derived(verdict(health));
	const color = $derived(toneColor(summary.tone));

	function fmt(iso: string | null) {
		return iso ? new Date(iso).toLocaleString() : 'never';
	}
</script>

<div
	class="mt-8 rounded-lg border px-4 py-3"
	style="border-color: var(--color-border); background: var(--color-surface)"
>
	<div class="flex items-baseline justify-between gap-3">
		<h2 class="font-medium">Health</h2>
		<span class="text-xs" style="color: var(--color-muted)">
			Last {health.windowDays} days
		</span>
	</div>

	<p class="mt-1 flex items-start gap-2 text-sm">
		<span class="mt-1.5 inline-block size-2 shrink-0 rounded-full" style={`background: ${color}`}
		></span>
		<span>{summary.message}</span>
	</p>

	<dl class="mt-3 grid grid-cols-2 gap-x-4 gap-y-3 text-sm sm:grid-cols-4">
		<div>
			<dt class="text-xs" style="color: var(--color-muted)">Success rate</dt>
			<dd class="tabular-nums" style={health.successRate === null ? '' : `color: ${color}`}>
				{health.successRate === null ? '—' : `${health.successRate}%`}
			</dd>
		</div>
		<div>
			<dt class="text-xs" style="color: var(--color-muted)">Runs</dt>
			<dd class="tabular-nums">
				{health.runs}
				{#if health.failed > 0}
					<span style="color: var(--color-muted)">({health.failed} failed)</span>
				{/if}
			</dd>
		</div>
		<div>
			<!-- Both averages are over successful runs only, so a bad week doesn't look like a
			     thin one. -->
			<dt class="text-xs" style="color: var(--color-muted)">Found a run</dt>
			<dd class="tabular-nums">{average(health.averageFound)}</dd>
		</div>
		<div>
			<dt class="text-xs" style="color: var(--color-muted)">Added a run</dt>
			<dd class="tabular-nums">{average(health.averageAdded)}</dd>
		</div>
	</dl>

	<p class="mt-3 text-xs" style="color: var(--color-muted)">
		Last run {fmt(health.lastRunAt)}{#if health.lastRunStatus}
			· {health.lastRunStatus.toLowerCase()}{/if}{#if health.emptyRuns > 0}
			· {health.emptyRuns}
			{health.emptyRuns === 1 ? 'run' : 'runs'} found nothing{/if}
	</p>

	{#if health.lastError}
		<pre
			class="mt-2 max-h-24 overflow-auto whitespace-pre-wrap break-words rounded p-2 text-xs"
			style="background: var(--color-bg); color: var(--color-danger)">{health.lastError}</pre>
	{/if}
</div>
