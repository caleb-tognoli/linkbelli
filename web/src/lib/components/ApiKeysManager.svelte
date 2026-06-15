<script lang="ts">
	import { api } from '$lib/api/client';
	import { Copy, Check, X, Key, Ban } from '@lucide/svelte';
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

	let name = $state('');
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
			/* clipboard blocked — user can still select the text manually */
		}
	}

	function toggleScope(scope: string, checked: boolean) {
		const next = new Set(selectedScopes);
		if (checked) next.add(scope);
		else next.delete(scope);
		selectedScopes = next;
	}

	async function create() {
		if (!name.trim() || busy) return;
		busy = true;
		error = null;
		try {
			const res = await api.post('/me/apikeys', {
				name: name.trim(),
				scopes: [...selectedScopes],
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
			name = '';
			selectedScopes = new Set();
		} finally {
			busy = false;
		}
	}

	async function revoke(key: ApiKey) {
		if (!confirm(`Revoke "${key.name}"? Apps using it will stop working.`)) return;
		const res = await api.del(`/me/apikeys/${key.id}`);
		if (res.ok || res.status === 204) keys = keys.filter((k) => k.id !== key.id);
	}

	function fmt(iso: string | null) {
		return iso ? new Date(iso).toLocaleDateString() : '—';
	}
</script>

<div class="flex flex-col gap-4">
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
						<Check size={13} aria-hidden="true" />
					{:else}
						<Copy size={13} aria-hidden="true" />
					{/if}
				</button>
				<button type="button" onclick={() => (createdToken = null)} title="Dismiss" aria-label="Dismiss" class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10">
					<X size={13} aria-hidden="true" />
				</button>
			</div>
		</div>
	{/if}

	<div class="rounded-lg border p-3" style="border-color: var(--color-border)">
		<div class="text-sm font-medium">Create a key</div>
		<div class="mt-2 flex flex-wrap items-end gap-3">
			<label class="flex flex-col gap-1 text-sm">
				<span>Name</span>
				<input bind:value={name} class="rounded-md border px-3 py-2 text-sm" style="border-color: var(--color-border); background: var(--color-bg)" />
			</label>
		</div>
		<fieldset class="mt-3">
			<legend class="text-xs" style="color: var(--color-muted)">Scopes (none = full access)</legend>
			<div class="mt-1 flex flex-wrap gap-3 text-sm">
				{#each ALL_SCOPES as scope (scope)}
					<label class="flex items-center gap-1">
						<input type="checkbox" checked={selectedScopes.has(scope)} onchange={(e) => toggleScope(scope, e.currentTarget.checked)} />
						<code>{scope}</code>
					</label>
				{/each}
			</div>
		</fieldset>
		{#if error}<p class="mt-2 text-sm" style="color: var(--color-danger)">{error}</p>{/if}
		<button type="button" onclick={create} disabled={busy || !name.trim()} class="mt-3 inline-flex items-center rounded-md p-2 disabled:opacity-60" style="background: var(--color-accent); color: var(--color-accent-contrast)" title="Create key" aria-label="Create key">
			<Key size={15} aria-hidden="true" />
		</button>
	</div>

	{#if keys.length === 0}
		<p class="text-sm" style="color: var(--color-muted)">No API keys yet.</p>
	{:else}
		<ul class="flex flex-col gap-2">
			{#each keys as key (key.id)}
				<li class="flex items-center justify-between gap-3 rounded-lg border p-3" style="border-color: var(--color-border); background: var(--color-surface)">
					<div class="min-w-0">
						<div class="font-medium">{key.name} <code class="text-xs" style="color: var(--color-muted)">{key.prefix}…</code></div>
						<div class="text-xs" style="color: var(--color-muted)">
							{key.scopes.length ? key.scopes.join(', ') : 'full access'} · created {fmt(key.creationTime)} · last used {fmt(key.lastUsedAt)}
						</div>
					</div>
					<button type="button" onclick={() => revoke(key)} title={`Revoke ${key.name}`} aria-label={`Revoke ${key.name}`} class="shrink-0 inline-flex items-center rounded p-1 hover:opacity-70" style="color: var(--color-danger)">
						<Ban size={14} aria-hidden="true" />
					</button>
				</li>
			{/each}
		</ul>
	{/if}
</div>
