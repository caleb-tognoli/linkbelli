<script lang="ts">
	import { api } from '$lib/api/client';
	import { Check, X } from '@lucide/svelte';
	import {
		onboardingProgress,
		onboardingSteps,
		shouldShowOnboarding,
		type OnboardingStep
	} from '$lib/onboarding';
	import type { Usage } from '$lib/types';

	let {
		usage = null,
		dismissed = false
	}: { usage?: Usage | null; dismissed?: boolean } = $props();

	// Hidden optimistically the moment it is dismissed, rather than after a round trip: the
	// request cannot fail in a way that makes keeping it on screen the right answer.
	let putAway = $state(dismissed);

	const steps = $derived(onboardingSteps(usage));
	const progress = $derived(onboardingProgress(steps));
	const visible = $derived(!putAway && shouldShowOnboarding(usage, false));

	async function dismiss() {
		putAway = true;
		await api.put('/me/preferences', { dismissOnboarding: true });
	}

	/** Ticked, or the step's number when it is still ahead. */
	function marker(step: OnboardingStep, index: number) {
		return step.done ? null : index + 1;
	}
</script>

{#if visible}
	<section
		class="rounded-lg border p-5"
		style="border-color: var(--color-border); background: var(--color-surface)"
		aria-label="Getting started"
	>
		<header class="flex items-start justify-between gap-3">
			<div>
				<h2 class="t-section">Getting started</h2>
				<p class="mt-0.5 text-sm" style="color: var(--color-muted)">
					{progress.done} of {progress.total} done. This goes away on its own once you have a
					playlist with something in it.
				</p>
			</div>
			<button
				type="button"
				onclick={dismiss}
				class="shrink-0 rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
				style="color: var(--color-muted)"
				title="Hide this for good"
				aria-label="Hide getting started"
			>
				<X size={16} aria-hidden="true" />
			</button>
		</header>

		<ol class="mt-4 flex flex-col gap-3">
			{#each steps as step, i (step.id)}
				<li class="flex items-start gap-3">
					<span
						class="mt-0.5 flex size-5 shrink-0 items-center justify-center rounded-full border text-xs tabular-nums"
						style={step.done
							? 'border-color: var(--color-success-solid); background: var(--color-success-solid); color: var(--color-on-solid)'
							: 'border-color: var(--color-border); color: var(--color-muted)'}
						aria-hidden="true"
					>
						{#if step.done}
							<Check size={12} />
						{:else}
							{marker(step, i)}
						{/if}
					</span>

					<div class="min-w-0 flex-1">
						<p class="text-sm font-medium" class:line-through={step.done} style={step.done ? 'color: var(--color-muted)' : ''}>
							{step.title}
							{#if !step.required}
								<!-- Said out loud, so an untickable box does not read as a failing grade. -->
								<span class="ml-1 text-xs font-normal" style="color: var(--color-muted)">optional</span>
							{/if}
						</p>
						{#if !step.done}
							<p class="mt-0.5 text-sm" style="color: var(--color-muted)">{step.body}</p>
							<a
								href={step.href}
								class="mt-1 inline-block text-sm underline underline-offset-2"
								style="color: var(--color-accent)"
							>
								{step.cta}
							</a>
						{/if}
					</div>
				</li>
			{/each}
		</ol>
	</section>
{/if}
