<script lang="ts" module>
	/** What a Field hands the control inside it, so the label, hint and error are wired to it. */
	export interface FieldControl {
		id: string;
		/** Ids of the hint and the error, for aria-describedby. Undefined when there are neither. */
		describedby: string | undefined;
		invalid: boolean;
		required: boolean;
	}
</script>

<script lang="ts">
	import type { Snippet } from 'svelte';

	/**
	 * A labelled form field: the label, the control, a hint under it and an error.
	 *
	 * Fields used to be put together by hand in every form — a label here, a caption there,
	 * a red paragraph somewhere near the button — so a hint was rarely tied to its input, an
	 * error was rarely announced, and "required" was never said. The control is passed in as a
	 * snippet that receives the id and description ids to put on it.
	 */
	let {
		label,
		hint,
		error = null,
		required = false,
		optional = false,
		hideLabel = false,
		id: givenId,
		class: extra = '',
		children
	}: {
		label: string;
		/** Help that stays on screen, read out with the field. */
		hint?: string;
		/** A problem with what was entered. Announced as it appears. */
		error?: string | null;
		/** Marks the label and tells assistive technology the field must be filled. */
		required?: boolean;
		/** Says "(optional)" beside the label, for forms where most fields are required. */
		optional?: boolean;
		/** Keeps the label for screen readers only, where the context already says it. */
		hideLabel?: boolean;
		id?: string;
		class?: string;
		children: Snippet<[FieldControl]>;
	} = $props();

	const uid = $props.id();
	const id = $derived(givenId ?? `${uid}-control`);
	const hintId = $derived(`${id}-hint`);
	const errorId = $derived(`${id}-error`);
	const describedby = $derived(
		[hint ? hintId : null, error ? errorId : null].filter(Boolean).join(' ') || undefined
	);
</script>

<div class="flex flex-col gap-1 text-sm {extra}">
	<label for={id} class={hideLabel ? 'sr-only' : ''}>
		{label}
		{#if required}
			<span aria-hidden="true" style="color: var(--color-danger)">*</span>
		{:else if optional}
			<span style="color: var(--color-muted)">(optional)</span>
		{/if}
	</label>
	{@render children({ id, describedby, invalid: !!error, required })}
	{#if hint}
		<p id={hintId} class="text-xs" style="color: var(--color-muted)">{hint}</p>
	{/if}
	{#if error}
		<p id={errorId} class="text-xs" style="color: var(--color-danger)" role="alert">{error}</p>
	{/if}
</div>
