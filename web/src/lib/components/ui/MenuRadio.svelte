<script lang="ts" generics="T extends string">
	import { DropdownMenu } from 'bits-ui';
	import type { Component } from 'svelte';
	import { Check } from '@lucide/svelte';
	import { MENU_ROW } from './MenuItem.svelte';

	/** A set of mutually exclusive options inside a Menu; the chosen one carries a tick. */
	let {
		value,
		options,
		onchange,
		heading
	}: {
		value: T;
		options: {
			value: T;
			label: string;
			description?: string;
			icon?: Component<{ size?: number; 'aria-hidden'?: boolean | 'true' }>;
		}[];
		onchange: (value: T) => void;
		/** A small heading above the options, when the menu holds more than one set. */
		heading?: string;
	} = $props();
</script>

<DropdownMenu.RadioGroup {value} onValueChange={(next) => onchange(next as T)}>
	{#if heading}
		<DropdownMenu.GroupHeading class="px-3 pt-1.5 pb-1 text-xs font-medium text-muted">
			{heading}
		</DropdownMenu.GroupHeading>
	{/if}
	{#each options as option (option.value)}
		<DropdownMenu.RadioItem value={option.value} class={MENU_ROW}>
			{#snippet children({ checked })}
				<span class="inline-flex w-4 shrink-0 justify-center text-accent">
					{#if checked}<Check size={14} aria-hidden="true" />{/if}
				</span>
				{#if option.icon}
					<span class="inline-flex shrink-0" style="color: var(--color-muted)">
						<option.icon size={15} aria-hidden="true" />
					</span>
				{/if}
				<span class="flex min-w-0 flex-col" class:font-medium={checked}>
					<span>{option.label}</span>
					{#if option.description}
						<span class="text-xs font-normal text-muted">{option.description}</span>
					{/if}
				</span>
			{/snippet}
		</DropdownMenu.RadioItem>
	{/each}
</DropdownMenu.RadioGroup>
