<script lang="ts">
	import { failureMessage } from '$lib/api/errors';
	import { toast } from '$lib/toast.svelte';
	import SecretReveal from '$lib/components/ui/SecretReveal.svelte';
	import Modal, { MODAL_FOOTER } from '$lib/components/ui/Modal.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Button, { buttonClass, iconSize } from '$lib/components/ui/Button.svelte';
	import { Dialog } from 'bits-ui';
	import { api } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import { Check, X, Plus, Ban } from '@lucide/svelte';
	import Switch from './Switch.svelte';
	import type { ApiKey, ApiKeyCreated } from '$lib/types';

	let { keys: initial }: { keys: ApiKey[] } = $props();
	let keys = $state(initial);

	const ALL_SCOPES = [
		'playlists:read',
		'playlists:write',
		'sources:read',
		'sources:write',
		'links:write'
	];

	const uid = $props.id();

	let dialogOpen = $state(false);
	let name = $state('');
	let fullAccess = $state(true);
	let selectedScopes = $state<Set<string>>(new Set());
	let busy = $state(false);
	let error = $state<string | null>(null);
	let createdToken = $state<ApiKeyCreated | null>(null);


	function toggleScope(scope: string, checked: boolean) {
		const next = new Set(selectedScopes);
		if (checked) next.add(scope);
		else next.delete(scope);
		selectedScopes = next;
	}

	function resetDialog() {
		name = '';
		fullAccess = true;
		selectedScopes = new Set();
		error = null;
	}

	function resetOnClose(o: boolean) {
		if (!o) resetDialog();
	}

	async function create() {
		if (!name.trim() || busy) return;
		busy = true;
		error = null;
		try {
			const res = await api.post('/me/apikeys', {
				name: name.trim(),
				scopes: fullAccess ? [] : [...selectedScopes],
				expiresAt: null
			});
			if (!res.ok) {
				error = 'Could not create the key.';
				return;
			}
			const created = (await res.json()) as ApiKeyCreated;
			createdToken = created;
			keys = [
				{
					id: created.id,
					name: created.name,
					prefix: created.prefix,
					scopes: created.scopes,
					creationTime: new Date().toISOString(),
					lastUsedAt: null,
					expiresAt: created.expiresAt
				},
				...keys
			];
			dialogOpen = false;
			resetDialog();
		} finally {
			busy = false;
		}
	}

	async function revoke(key: ApiKey) {
		if (!(await confirmDialog(`Revoke "${key.name}"? Apps using it will stop working.`, { danger: true, confirmLabel: 'Revoke' }))) return;
		const res = await api.del(`/me/apikeys/${key.id}`);
		if (res.ok || res.status === 204) keys = keys.filter((k) => k.id !== key.id);
		else toast.error(failureMessage(res.status, 'Could not revoke that key.'));
	}

	function fmt(iso: string | null) {
		return iso ? new Date(iso).toLocaleDateString() : 'Never';
	}
</script>

<div class="flex flex-col gap-4">
	<div class="flex items-center justify-between">
		<h3 class="t-section">API keys</h3>
		<Modal bind:open={dialogOpen} onOpenChange={resetOnClose} title="New API key">
			{#snippet trigger()}
				<Dialog.Trigger class={buttonClass('secondary', 'sm')}>
					<Plus size={iconSize('sm')} aria-hidden="true" />
					New API key
				</Dialog.Trigger>
			{/snippet}

			<div class="flex flex-col gap-4">
				<Field label="Name">
					{#snippet children(f)}
						<Input
							id={f.id}
							bind:value={name}
							aria-describedby={f.describedby}
						/>
					{/snippet}
				</Field>

				<label class="flex items-center gap-2 text-sm">
					<Switch checked={fullAccess} onchange={(v) => (fullAccess = v)} labelledby="{uid}-full-access" />
					<span id="{uid}-full-access">Full access</span>
				</label>

				{#if !fullAccess}
					<div class="grid grid-cols-2 gap-x-6 gap-y-2.5 text-sm">
						{#each ALL_SCOPES as scope (scope)}
							<label class="flex items-center gap-1.5">
								<Switch
									checked={selectedScopes.has(scope)}
									onchange={(v) => toggleScope(scope, v)}
									label={scope}
								/>
								<code>{scope}</code>
							</label>
						{/each}
					</div>
				{/if}

				{#if error}
					<p class="text-sm" style="color: var(--color-danger)" role="alert">{error}</p>
				{/if}

				<div class={MODAL_FOOTER}>
					<Dialog.Close class={buttonClass('secondary')}>
						<X size={17} aria-hidden="true" /> Cancel
					</Dialog.Close>
					<Button variant="primary" icon={Check} onclick={create} loading={busy} disabled={!name.trim()}>
						{busy ? 'Creating…' : 'Create key'}
					</Button>
				</div>
			</div>
		</Modal>
	</div>

	{#if createdToken}
		<SecretReveal
			title="Copy your new key now — it won't be shown again."
			value={createdToken.token}
			label="New API key"
			ondismiss={() => (createdToken = null)}
		/>
	{/if}

	{#if keys.length === 0}
		<p class="text-sm" style="color: var(--color-muted)">No API keys yet.</p>
	{:else}
		<ul class="flex flex-col gap-2">
			{#each keys as key (key.id)}
				<!-- The row was one unwrapping line, so a long key name pushed its prefix and last
				     use off the side of a phone. The metadata drops below the name instead. -->
				<li class="flex items-center gap-3 rounded-card border bg-surface px-3 py-2.5 text-sm">
					<div class="min-w-0 flex-1">
						<span class="block truncate font-medium">{key.name}</span>
						<span class="mt-0.5 flex flex-wrap items-center gap-x-3 text-xs text-muted">
							<code>{key.prefix}…</code>
							<span>Last used {fmt(key.lastUsedAt)}</span>
						</span>
					</div>
					<Button
						variant="ghost-danger"
						size="sm"
						icon={Ban}
						iconOnly
						label={`Revoke ${key.name}`}
						onclick={() => revoke(key)}
					/>
				</li>
			{/each}
		</ul>
	{/if}
</div>
