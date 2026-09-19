<script lang="ts">
	import CopyField from '$lib/components/ui/CopyField.svelte';
	import Modal from '$lib/components/ui/Modal.svelte';
	import Select from '$lib/components/ui/Select.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import Button, { buttonClass } from '$lib/components/ui/Button.svelte';
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

	let inviteLink = $state<string | null>(null);
	let inviteEmailed = $state(false);
	let inviting = $state(false);

	/**
	 * A link for somebody who is not here yet.
	 *
	 * Adding by username needs them to already have an account and needs you to know its exact
	 * name — which, on an instance somebody just stood up, nobody does. The link comes back
	 * whether or not it was emailed, because mail is optional on this product and an instance
	 * without it still has to be able to invite people.
	 */
	async function invite() {
		inviting = true;
		error = null;
		try {
			const address = username.trim();
			const res = await api.post(`/playlists/${playlistId}/invites`, {
				// The same box. Somebody typing an address plainly means "send it to them", and
				// making them find a second field to say so would be the app being pedantic.
				email: address.includes('@') ? address : null,
				role
			});

			if (!res.ok) {
				error = await problem(res);
				return;
			}

			const created = (await res.json()) as { url: string; emailed: boolean };
			inviteLink = created.url;
			inviteEmailed = created.emailed;
			username = '';
		} finally {
			inviting = false;
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

</script>

<Modal
	bind:open
	title="Share this playlist"
	description="With specific people, by username — or with a link, for somebody who has no account here yet. Making it public is a separate decision."
>
	{#snippet trigger()}
		<Dialog.Trigger class={buttonClass('secondary', 'sm')} title="Share with specific people">
			<UserPlus size={15} aria-hidden="true" />
			Share
		</Dialog.Trigger>
	{/snippet}

	<div class="flex shrink-0 gap-2">
		<Input
			bind:value={username}
			onkeydown={(e) => e.key === 'Enter' && share()}
			placeholder="username or email"
			aria-label="Username or email address"
			class="min-w-0 flex-1"
		/>
		<Select bind:value={role} aria-label="Role">
			{#each roles as option (option.value)}
				<option value={option.value}>{option.label}</option>
			{/each}
		</Select>
		<Button variant="primary" onclick={share} loading={busy} disabled={!username.trim()}>Add</Button>
	</div>

	<p class="mt-1.5 shrink-0 text-xs" style="color: var(--color-muted)">
		{roles.find((r) => r.value === role)?.hint}
	</p>

	<div class="mt-2 shrink-0">
		<button
			type="button"
			onclick={invite}
			disabled={inviting}
			class="text-xs underline underline-offset-2 disabled:opacity-60"
			style="color: var(--color-accent)"
		>
			{inviting ? 'Making a link…' : 'Invite by link instead'}
		</button>
		{#if username.includes('@')}
			<span class="ml-1 text-xs" style="color: var(--color-muted)">— and email it there</span>
		{/if}
	</div>

	{#if inviteLink}
		<div
			class="mt-2 shrink-0 rounded-md border p-2.5 text-xs"
			style="border-color: var(--color-border)"
		>
			<p style="color: var(--color-muted)">
				{inviteEmailed
					? 'Sent. The link also works if you would rather pass it on yourself:'
					: 'Copy this and send it however you like. It works once, and lasts two weeks.'}
			</p>
			<CopyField value={inviteLink} label="Invitation link" class="mt-1.5" />
		</div>
	{/if}

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
						<Select
							value={member.role}
							onchange={(e) => setRole(member, e.currentTarget.value as PlaylistRole)}
							aria-label={`Role for ${member.username}`}
							size="sm"
						>
							{#each roles as option (option.value)}
								<option value={option.value}>{option.label}</option>
							{/each}
						</Select>
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
</Modal>
