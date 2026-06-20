<script lang="ts">
	import { Dialog } from 'bits-ui';
	import { api } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import { Copy, Check, X, Plus, Ban } from '@lucide/svelte';
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

	let dialogOpen = $state(false);
	let name = $state('');
	let fullAccess = $state(true);
	let selectedScopes = $state<Set<string>>(new Set());
	let busy = $state(false);
	let error = $state<string | null>(null);
	let createdToken = $state<ApiKeyCreated | null>(null);
	let copied = $state(false);

	async function copyToken() {
		if (!createdToken) return;
		try {
			await navigator.clipboard.writeText(createdToken.token);
			copied = true;
			setTimeout(() => (copied = false), 2000);
		} catch {
			/* clipboard blocked */
		}
	}

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
	}

	function fmt(iso: string | null) {
		return iso ? new Date(iso).toLocaleDateString() : 'Never';
	}
</script>

<div class="flex flex-col gap-4">
	<div class="flex items-center justify-between">
		<h2 class="font-medium">API keys</h2>
		<Dialog.Root
			bind:open={dialogOpen}
			onOpenChange={(o) => {
				if (!o) resetDialog();
			}}
		>
			<Dialog.Trigger
				class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
				title="New API key"
				aria-label="New API key"
			>
				<Plus size={18} aria-hidden="true" />
			</Dialog.Trigger>

			<Dialog.Portal>
				<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
				<Dialog.Content
					class="fixed left-1/2 top-1/2 z-50 w-[90vw] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-xl border p-6 shadow-xl"
					style="border-color: var(--color-border); background: var(--color-surface)"
				>
					<Dialog.Title class="text-lg font-semibold">New API key</Dialog.Title>

					<div class="mt-4 flex flex-col gap-4">
						<label class="flex flex-col gap-1 text-sm">
							<span>Name</span>
							<input
								bind:value={name}
								class="rounded-md border px-3 py-2 text-sm"
								style="border-color: var(--color-border); background: var(--color-bg)"
							/>
						</label>

						<label class="flex items-center gap-2 text-sm">
							<Switch checked={fullAccess} onchange={(v) => (fullAccess = v)} />
							Full Access
						</label>

						{#if !fullAccess}
							<div class="grid grid-cols-2 gap-x-6 gap-y-2.5 text-sm">
								{#each ALL_SCOPES as scope (scope)}
									<label class="flex items-center gap-1.5">
										<Switch
											checked={selectedScopes.has(scope)}
											onchange={(v) => toggleScope(scope, v)}
										/>
										<code>{scope}</code>
									</label>
								{/each}
							</div>
						{/if}

						{#if error}
							<p class="text-sm" style="color: var(--color-danger)">{error}</p>
						{/if}

						<div class="mt-2 flex justify-center gap-2 text-sm">
							<Dialog.Close
								class="inline-flex items-center gap-1.5 rounded-md border px-3 py-1.5 hover:bg-black/5 dark:hover:bg-white/10"
								style="border-color: var(--color-border)"
							>
								<X size={15} aria-hidden="true" /> Cancel
							</Dialog.Close>
							<button
								type="button"
								onclick={create}
								disabled={busy || !name.trim()}
								class="inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 font-medium disabled:opacity-60"
								style="background: var(--color-accent); color: var(--color-accent-contrast)"
							>
								<Check size={15} aria-hidden="true" />
								{busy ? 'Creating…' : 'Create'}
							</button>
						</div>
					</div>
				</Dialog.Content>
			</Dialog.Portal>
		</Dialog.Root>
	</div>

	{#if createdToken}
		<div class="rounded-md border p-3 text-sm" style="border-color: var(--color-accent)">
			<p class="font-medium">Copy your new key now — it won't be shown again:</p>
			<code class="mt-2 block break-all rounded p-2" style="background: var(--color-surface)">{createdToken.token}</code>
			<div class="mt-2 flex items-center gap-3">
				<button
					type="button"
					class="inline-flex items-center rounded-md p-1.5"
					style="background: var(--color-accent); color: var(--color-accent-contrast)"
					onclick={copyToken}
					title={copied ? 'Copied!' : 'Copy key'}
					aria-label={copied ? 'Copied!' : 'Copy key'}
				>
					{#if copied}
						<Check size={15} aria-hidden="true" />
					{:else}
						<Copy size={15} aria-hidden="true" />
					{/if}
				</button>
				<button
					type="button"
					onclick={() => (createdToken = null)}
					title="Dismiss"
					aria-label="Dismiss"
					class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
				>
					<X size={15} aria-hidden="true" />
				</button>
			</div>
		</div>
	{/if}

	{#if keys.length === 0}
		<p class="text-sm" style="color: var(--color-muted)">No API keys yet.</p>
	{:else}
		<ul class="flex flex-col gap-2">
			{#each keys as key (key.id)}
				<li
					class="flex items-center gap-4 rounded-lg border px-3 py-2.5 text-sm"
					style="border-color: var(--color-border); background: var(--color-surface)"
				>
					<span class="font-medium">{key.name}</span>
					<div class="flex-1"></div>
					<div class="flex items-center gap-4 text-xs leading-none" style="color: var(--color-muted)">
						<code>{key.prefix}…</code>
						<span>Last used {fmt(key.lastUsedAt)}</span>
					</div>
					<button
						type="button"
						onclick={() => revoke(key)}
						title="Revoke {key.name}"
						aria-label="Revoke {key.name}"
						class="inline-flex items-center rounded p-1 hover:opacity-70"
						style="color: var(--color-danger)"
					>
						<Ban size={16} aria-hidden="true" />
					</button>
				</li>
			{/each}
		</ul>
	{/if}
</div>
