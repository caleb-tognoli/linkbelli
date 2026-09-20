<script lang="ts">
	import Input from '$lib/components/ui/Input.svelte';
	import { api, json } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import { Search, ShieldCheck, ShieldOff, UserCheck, UserX } from '@lucide/svelte';
	import type { AdminUser } from '$lib/types';

	let { me }: { me: string | null } = $props();

	let term = $state('');
	let users = $state<AdminUser[]>([]);
	let searching = $state(false);
	let error = $state<string | null>(null);
	let busy = $state<string | null>(null);

	async function search() {
		searching = true;
		error = null;
		try {
			// Built up rather than nested inside one template literal: Svelte's parser mis-reads a
			// backtick inside a `${}` and quietly falls back to legacy mode, which turns every
			// rune in the file into a warning about not using runes.
			const query = new URLSearchParams({ limit: '25' });
			if (term.trim()) query.set('q', term.trim());

			users = await json<AdminUser[]>(await api.get(`/admin/users?${query}`));
		} catch {
			error = 'Could not search.';
		} finally {
			searching = false;
		}
	}

	async function act(user: AdminUser, run: () => Promise<Response>, failure: string) {
		busy = user.id;
		error = null;
		const res = await run();
		busy = null;

		if (res.ok) await search();
		else if (res.status === 400) error = 'You cannot remove your own administrator access.';
		else error = failure;
	}

	/**
	 * Suspending keeps everything and blocks the door; deleting is the owner's own decision and
	 * is not offered here. An admin needs to be able to tell "I turned this off" from "they
	 * left", which is why the two are separate states rather than one.
	 */
	async function toggleSuspended(user: AdminUser) {
		const suspending = !user.suspendedAt;
		if (suspending) {
			const ok = await confirmDialog(
				`Suspend ${user.username}? They will not be able to sign in, and anything they published comes down. Nothing is deleted, and reinstating puts it all back.`,
				{ danger: true, confirmLabel: 'Suspend' }
			);
			if (!ok) return;
		}

		await act(
			user,
			() => api.put(`/admin/users/${user.id}/suspended`, { suspended: suspending }),
			'Could not change that.'
		);
	}

	async function toggleAdmin(user: AdminUser) {
		const granting = !user.isAdmin;
		const ok = await confirmDialog(
			granting
				? `Make ${user.username} an administrator? They will be able to see every account, block hosts, and do this to other people.`
				: `Take administrator access away from ${user.username}?`,
			{ danger: !granting, confirmLabel: granting ? 'Make admin' : 'Remove admin' }
		);
		if (!ok) return;

		await act(
			user,
			() => api.put(`/admin/users/${user.id}/admin`, { admin: granting }),
			'Could not change that.'
		);
	}

	/**
	 * Not called `state`: a local binding of that name turns every `$state(...)` in the file into
	 * a store subscription on it, and the compiler reports the runes as the problem.
	 */
	function standing(user: AdminUser): string | null {
		if (user.suspendedAt) return 'suspended';
		if (user.deletionRequestedAt) return 'leaving';
		return null;
	}
</script>

<h2 class="t-section">Accounts</h2>
<p class="mt-1 text-sm" style="color: var(--color-muted)">
	Suspending keeps everything and blocks the door. Deleting an account is its owner's decision,
	made from their own settings.
</p>

<form
	class="mt-3 flex gap-2"
	onsubmit={(e) => {
		e.preventDefault();
		void search();
	}}
>
	<Input
		icon={Search}
		class="flex-1"
		bind:value={term}
		placeholder="Find an account by name or address"
		aria-label="Find an account"
	/>
	<Button type="submit" icon={Search} loading={searching}>
		{searching ? 'Looking…' : 'Search'}
	</Button>
</form>

{#if error}
	<p class="mt-2 text-sm" style="color: var(--color-danger)" role="alert">{error}</p>
{/if}

{#if users.length > 0}
	<ul class="mt-3 flex flex-col divide-y rounded-card border" style="border-color: var(--color-border)">
		{#each users as user (user.id)}
			<li class="flex flex-wrap items-center gap-3 p-3" style="border-color: var(--color-border)">
				<div class="min-w-0 flex-1">
					<p class="truncate text-sm font-medium">
						{user.username}
						{#if user.isAdmin}
							<span class="ml-1 text-xs font-normal" style="color: var(--color-accent)">admin</span>
						{/if}
						{#if standing(user)}
							<span class="ml-1 text-xs font-normal" style="color: var(--color-warning)">{standing(user)}</span>
						{/if}
					</p>
					<p class="truncate text-xs" style="color: var(--color-muted)">
						{user.playlistCount} playlists · {user.sourceCount} sources
					</p>
				</div>

				<div class="flex shrink-0 items-center gap-1">
					<button
						type="button"
						onclick={() => toggleAdmin(user)}
						disabled={busy !== null || user.username === me}
						class="inline-flex items-center rounded-control border p-1.5 disabled:opacity-40"
						style="border-color: var(--color-border)"
						title={user.username === me
							? 'You cannot change your own administrator access'
							: user.isAdmin
								? 'Remove administrator access'
								: 'Make an administrator'}
						aria-label={user.isAdmin ? `Remove admin from ${user.username}` : `Make ${user.username} an admin`}
					>
						{#if user.isAdmin}
							<ShieldOff size={15} aria-hidden="true" />
						{:else}
							<ShieldCheck size={15} aria-hidden="true" />
						{/if}
					</button>
					<button
						type="button"
						onclick={() => toggleSuspended(user)}
						disabled={busy !== null || user.username === me}
						class="inline-flex items-center rounded-control border p-1.5 disabled:opacity-40"
						style="border-color: var(--color-border); color: {user.suspendedAt
							? 'inherit'
							: 'var(--color-danger)'}"
						title={user.username === me
							? 'You cannot suspend yourself'
							: user.suspendedAt
								? 'Let them back in'
								: 'Block sign-in and hide what they published'}
						aria-label={user.suspendedAt ? `Reinstate ${user.username}` : `Suspend ${user.username}`}
					>
						{#if user.suspendedAt}
							<UserCheck size={15} aria-hidden="true" />
						{:else}
							<UserX size={15} aria-hidden="true" />
						{/if}
					</button>
				</div>
			</li>
		{/each}
	</ul>
{/if}
