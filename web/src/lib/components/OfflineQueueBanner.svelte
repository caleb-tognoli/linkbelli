<script lang="ts">
	import { CloudOff, RefreshCw, X } from '@lucide/svelte';
	import { offlineSaves } from '$lib/offlineSaves.svelte';

	// Shown wherever the person is, because the thing it reports happened somewhere else — on a
	// share sheet that has since closed — and there is otherwise no trace of it.
	const summary = $derived(offlineSaves.summary);
</script>

{#if summary}
	<div
		class="mb-4 flex flex-wrap items-center gap-x-3 gap-y-2 rounded-card border px-3 py-2 text-sm"
		style="border-color: var(--color-warning); background: var(--color-surface)"
		role="status"
	>
		<CloudOff size={16} aria-hidden="true" style="color: var(--color-warning)" />
		<span class="min-w-0 flex-1">{summary}</span>

		{#if offlineSaves.waiting > 0}
			<button
				type="button"
				onclick={() => offlineSaves.flush()}
				disabled={offlineSaves.flushing}
				class="inline-flex items-center gap-1.5 rounded-control border px-2 py-1 text-xs disabled:opacity-60"
				style="border-color: var(--color-border)"
			>
				<RefreshCw size={12} aria-hidden="true" />
				{offlineSaves.flushing ? 'Sending…' : 'Send now'}
			</button>
		{/if}
	</div>

	{#if offlineSaves.stuck.length > 0}
		<!-- Named one by one, because "1 could not be saved" without saying which leaves somebody
		     with no way to do anything about it. -->
		<ul class="mb-4 flex flex-col gap-1 text-xs" style="color: var(--color-muted)">
			{#each offlineSaves.stuck as save (save.id)}
				<li class="flex items-center gap-2">
					<span class="min-w-0 flex-1 truncate" title={save.url}>{save.url}</span>
					<span class="shrink-0">→ {save.playlistName}</span>
					<button
						type="button"
						onclick={() => offlineSaves.drop(save.id)}
						class="inline-flex items-center justify-center rounded-control size-6 tap-target shrink-0 hover:bg-black/5 dark:hover:bg-white/10"
						title="Forget this one"
						aria-label={`Forget ${save.url}`}
					>
						<X size={12} aria-hidden="true" />
					</button>
				</li>
			{/each}
		</ul>
	{/if}
{/if}
