<script lang="ts">
	import { Check, Circle } from '@lucide/svelte';
	import { PASSWORD_RULES } from '$lib/accountRules';

	/**
	 * What a new password still needs, ticked off as it is typed.
	 *
	 * Every rule at once, before sending, instead of the one the server happened to check first.
	 */
	let { password, id }: { password: string; id: string } = $props();
</script>

<ul {id} class="flex flex-col gap-0.5 text-xs">
	{#each PASSWORD_RULES as rule (rule.label)}
		{@const met = rule.met(password)}
		<li class="flex items-center gap-1.5 {met ? 'text-success' : 'text-muted'}">
			{#if met}
				<Check size={13} aria-hidden="true" />
			{:else}
				<Circle size={13} aria-hidden="true" />
			{/if}
			{rule.label}
			<span class="sr-only">{met ? '(done)' : '(still needed)'}</span>
		</li>
	{/each}
</ul>
