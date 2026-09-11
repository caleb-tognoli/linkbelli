<script lang="ts">
	import { Dialog } from 'bits-ui';
	import { api } from '$lib/api/client';
	import { UserPlus, X } from '@lucide/svelte';
	import type { PlaylistMember, PlaylistRole } from '$lib/types';

	let { playlistId }: { playlistId: string } = $props();

	const roles: { value: PlaylistRole; label: string; hint: string }[] = [
		{ value: 'Viewer', label: 'Viewer', hint: 'Can read it' },
		{ value: 'Contributor', label: 'Contributor', hint: 'Can also add links' },
		{ value: 'Editor', label: 'Editor', hint: 'Can also edit and remove' }
	];

	let open = $state(false);
	let members = $state<PlaylistMember[]>([]);
	let username = $state('');
	let role = $state<PlaylistRole>('Viewer');
	let busy = $state(false);
	let error = $state<string | null>(null);

	// Loaded when the dialog opens rather than with the page: most playlists are shared with
	// nobody, and this is a request per playlist view otherwise.
	$effect(() => {
		if (!open) return;
		void load();
	});

	async function load() {
		const res = await api.get(`/playlists/${playlistId}/members`);
		if (res.ok) members = (await res.json()) as PlaylistMember[];
	}

	/** The server's own words when it has any — "no such user" is worth saying exactly. */
	async function problem(res: Response): Promise<string> {
		try {
			const body = (await res.json()) as { errors?: Record<string, string[]>; detail?: string };
			return Object.values(body.errors ?? {})[0]?.[0] ?? body.detail ?? 'Could not share it.';
		} catch {
			return 'Could not share it.';
		}
	}

	async function share() {
		const name = username.trim();
		if (!name) return;

		busy = true;
		error = null;
		try {
			const res = await api.put(`/playlists/${playlistId}/members/${encodeURIComponent(name)}`, { role });
			if (!res.ok) {
				error = await problem(res);
				return;
			}

			username = '';
			await load();
		} finally {
			busy = false;
		}
	}

	async function setRole(member: PlaylistMember, value: PlaylistRole) {
		const res = await api.put(
			`/playlists/${playlistId}/members/${encodeURIComponent(member.username)}`,
			{ role: value }
		);
		if (res.ok) await load();
	}

	async function remove(member: PlaylistMember) {
		const res = await api.del(`/playlists/${playlistId}/members/${encodeURIComponent(member.username)}`);
		if (res.ok || res.status === 204) await load();
	}

	const fieldStyle = 'border-color: var(--color-border); background: var(--color-bg)';
</script>

<Dialog.Root bind:open>
	<Dialog.Trigger
		class="inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-xs"
		style="border-color: var(--color-border); color: var(--color-muted)"
		title="Share with specific people"
	>
		<UserPlus size={13} aria-hidden="true" />
		Share
	</Dialog.Trigger>
	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
		<Dialog.Content
			class="fixed left-1/2 top-1/2 z-50 flex max-h-[70vh] w-[90vw] max-w-md -translate-x-1/2 -translate-y-1/2 flex-col rounded-xl border p-5 shadow-xl"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<div class="flex shrink-0 items-center justify-between">
				<Dialog.Title class="font-semibold">Share this playlist</Dialog.Title>
				<Dialog.Close class="rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10" title="Close" aria-label="Close">
					<X size={17} aria-hidden="true" />
				</Dialog.Close>
			</div>

			<p class="mt-1 shrink-0 text-sm" style="color: var(--color-muted)">
				With specific people, by username. Making it public is a separate decision.
			</p>

			<div class="mt-3 flex shrink-0 gap-2">
				<input
					bind:value={username}
					onkeydown={(e) => e.key === 'Enter' && share()}
					placeholder="username"
					aria-label="Username"
					class="min-w-0 flex-1 rounded-md border px-3 py-2 text-sm"
					style={fieldStyle}
				/>
				<select bind:value={role} aria-label="Role" class="rounded-md border px-2 py-2 text-sm" style={fieldStyle}>
					{#each roles as option (option.value)}
						<option value={option.value}>{option.label}</option>
					{/each}
				</select>
				<button
					type="button"
					onclick={share}
					disabled={busy || !username.trim()}
					class="shrink-0 rounded-md px-3 py-2 text-sm font-medium disabled:opacity-60"
					style="background: var(--color-accent); color: var(--color-accent-contrast)"
				>Add</button>
			</div>

			<p class="mt-1.5 shrink-0 text-xs" style="color: var(--color-muted)">
				{roles.find((r) => r.value === role)?.hint}
			</p>

			{#if error}
				<p class="mt-2 shrink-0 text-sm" style="color: var(--color-danger)">{error}</p>
			{/if}

			<div class="mt-3 flex-1 overflow-y-auto">
				{#if members.length === 0}
					<p class="py-2 text-sm" style="color: var(--color-muted)">Not shared with anyone yet.</p>
				{:else}
					<ul class="flex flex-col gap-1">
						{#each members as member (member.username)}
							<li class="flex items-center gap-2 rounded-md px-1 py-1.5 text-sm">
								<span class="min-w-0 flex-1 truncate">@{member.username}</span>
								<select
									value={member.role}
									onchange={(e) => setRole(member, e.currentTarget.value as PlaylistRole)}
									aria-label={`Role for ${member.username}`}
									class="rounded-md border px-2 py-1 text-xs"
									style={fieldStyle}
								>
									{#each roles as option (option.value)}
										<option value={option.value}>{option.label}</option>
									{/each}
								</select>
								<button
									type="button"
									onclick={() => remove(member)}
									class="rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
									style="color: var(--color-danger)"
									title={`Remove ${member.username}`}
									aria-label={`Remove ${member.username}`}
								>
									<X size={15} aria-hidden="true" />
								</button>
							</li>
						{/each}
					</ul>
				{/if}
			</div>
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
