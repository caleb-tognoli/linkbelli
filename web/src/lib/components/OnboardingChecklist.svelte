<script lang="ts">
	import { api } from '$lib/api/client';
	import { Check, X } from '@lucide/svelte';
	import Button from './ui/Button.svelte';
	import {
		nextStep,
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

	/**
	 * Ticked, or the step's number when it is still ahead — counting only the steps the number
	 * above counts.
	 *
	 * The heading said "0 of 2 done" over a list numbered 1 to 4, because the progress line counts
	 * the two required steps and the markers used each step's place in the whole list. Two numbers
	 * about the same list, disagreeing. The optional ones keep a dot: they are things to do, not
	 * steps to get through.
	 */
	const numbered = $derived(steps.filter((s) => s.required));
	function marker(step: OnboardingStep): number | null {
		if (step.done || !step.required) return null;
		return numbered.indexOf(step) + 1;
	}

	/** The first thing still to do, which is the one worth a real button. */
	const next = $derived(nextStep(steps));
</script>

{#if visible}
	<section
		class="rounded-card border p-5"
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
				class="shrink-0 rounded-control p-1 hover:bg-black/5 dark:hover:bg-white/10"
				style="color: var(--color-muted)"
				title="Hide this for good"
				aria-label="Hide getting started"
			>
				<X size={16} aria-hidden="true" />
			</button>
		</header>

		<ol class="mt-4 flex flex-col gap-3">
			{#each steps as step (step.id)}
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
						{:else if marker(step) !== null}
							{marker(step)}
						{:else}
							<span class="size-1 rounded-full bg-border-strong"></span>
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
							<!-- The step somebody is actually on gets a button. The four highest-intent
							     actions a new account can take were four small underlined links inside
							     body text, under a header carrying two filled buttons for lower-intent
							     ones. -->
							<div class="mt-2">
								<Button
									href={step.href}
									size="sm"
									variant={step.id === next?.id ? 'primary' : 'secondary'}
								>
									{step.cta}
								</Button>
							</div>
						{/if}
					</div>
				</li>
			{/each}
		</ol>
	</section>
{/if}
