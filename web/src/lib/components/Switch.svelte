<script lang="ts">
	let {
		checked = $bindable(false),
		onchange,
		disabled = false,
		label,
		labelledby
	}: {
		checked?: boolean;
		onchange?: (value: boolean) => void;
		disabled?: boolean;
		/** What this switch controls. Announced as the control's name. */
		label?: string;
		/** Id of the visible text that already names it, when there is one. */
		labelledby?: string;
	} = $props();

	function toggle() {
		if (disabled) return;
		checked = !checked;
		onchange?.(checked);
	}
</script>

<button
	type="button"
	role="switch"
	aria-checked={checked}
	aria-label={labelledby ? undefined : label}
	aria-labelledby={labelledby}
	{disabled}
	onclick={toggle}
	class="relative inline-flex h-5 w-9 shrink-0 rounded-full transition-colors duration-200 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
	style={checked
		? 'background: var(--color-accent); outline-color: var(--color-accent)'
		: 'background: var(--color-border)'}
>
	<span
		class="my-0.5 inline-block h-4 w-4 rounded-full bg-white shadow-sm transition-transform duration-200"
		style={checked ? 'transform: translateX(1.125rem)' : 'transform: translateX(0.125rem)'}
	></span>
</button>
