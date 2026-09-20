<script lang="ts">
	import SkeletonRows from '$lib/components/ui/SkeletonRows.svelte';
	import { api, json } from '$lib/api/client';
	import Switch from '$lib/components/Switch.svelte';
	import type { NotificationPrefs as Prefs } from '$lib/notifications';

	/**
	 * Described by what arrives, not by a setting name.
	 *
	 * "Share notifications" tells somebody nothing about how often their inbox will ring, which is
	 * the only thing they actually want to know before deciding.
	 */
	const ROWS = [
		{
			key: 'onShare' as const,
			label: 'Somebody shares a playlist with me',
			hint: 'Rare, and you would not find out any other way.'
		},
		{
			key: 'onSourceStopped' as const,
			label: 'One of my sources stops working',
			hint: 'Sent once, when a source gives up after repeated failures.'
		},
		{
			key: 'onFollow' as const,
			label: 'Somebody follows one of my playlists',
			hint: 'Can happen often on a public playlist.'
		},
		{
			key: 'weeklyDigest' as const,
			label: 'A weekly summary',
			hint: 'What arrived, what is still unread, and any source that went quiet.'
		}
	];

	let {
		email = null,
		confirmed = true,
		initial = null
	}: {
		/** What the page already fetched, so this does not blink "Looking…" on the way in. */
		initial?: Prefs | null;
		/** The address all of this would go to. Shown only when it cannot be written to. */
		email?: string | null;
		/**
		 * Whether that address has been confirmed.
		 *
		 * Defaults to true so a caller that does not know says nothing, rather than accusing
		 * somebody of not having confirmed an address they confirmed years ago.
		 */
		confirmed?: boolean;
	} = $props();

	let prefs = $state<Prefs | null>(initial);
	let error = $state<string | null>(null);

	let resending = $state(false);
	let resent = $state(false);

	async function resendConfirmation() {
		if (!email) return;

		resending = true;
		const res = await api.post('/auth/resend-confirmation', { email });
		resending = false;
		resent = res.ok;
	}

	// Only when the page could not get them; otherwise they are already here.
	$effect(() => {
		if (!prefs) void load();
	});

	async function load() {
		try {
			prefs = await json<Prefs>(await api.get('/notifications'));
			error = null;
		} catch {
			error = 'Could not load these.';
		}
	}

	let previewing = $state(false);
	let previewNote = $state<string | null>(null);

	async function sendPreview() {
		previewing = true;
		previewNote = null;
		const res = await api.post('/notifications/digest/preview', {});
		previewing = false;
		previewNote = res.ok
			? 'Sent — have a look in your inbox.'
			: 'Could not send that. This Linkbelli may have no mail set up.';
	}

	async function set(key: keyof Prefs, value: boolean) {
		if (!prefs) return;

		const previous = prefs[key];
		prefs = { ...prefs, [key]: value };

		// Only the one that changed. Every field is optional on the API, so this cannot reach
		// across and undo another switch.
		const res = await api.put('/notifications', { [key]: value });
		if (!res.ok) {
			prefs = { ...prefs, [key]: previous };
			error = 'Could not save that.';
		}
	}
</script>

<div>
	<h3 class="t-section">Notifications</h3>
	<p class="mt-1 max-w-prose text-sm" style="color: var(--color-muted)">
		Nothing here is marketing, and every message carries a link that turns that kind off. The
		two about your own things are on; the two that repeat are not, unless you say so.
	</p>

	{#if !confirmed}
		<!-- Everything below is switched on and does nothing. Said here rather than left to be
		     discovered, because "my notifications are broken" is the obvious conclusion. -->
		<div class="mt-3 rounded-control border p-3 text-sm" style="border-color: var(--color-warning)">
			<p>
				Nothing is sent to {email ?? 'your address'} until you confirm it. Look for the message
				from when you signed up, or ask for another.
			</p>
			{#if resent}
				<p class="mt-2" style="color: var(--color-muted)">Another one is on its way.</p>
			{:else}
				<button
					type="button"
					onclick={resendConfirmation}
					disabled={resending || !email}
					class="mt-2 rounded-control border px-2.5 py-1.5 text-sm disabled:opacity-60"
					style="border-color: var(--color-border)"
				>
					{resending ? 'Sending…' : 'Send another link'}
				</button>
			{/if}
		</div>
	{/if}

	{#if error}
		<p class="mt-3 text-sm" style="color: var(--color-danger)" role="alert">{error}</p>
	{/if}

	{#if prefs}
		<ul class="mt-3 flex flex-col gap-3">
			{#each ROWS as row (row.key)}
				<li class="flex items-start gap-3">
					<div class="mt-0.5 shrink-0">
						<Switch checked={prefs[row.key]} onchange={(v) => set(row.key, v)} label={row.label} />
					</div>
					<div class="min-w-0">
						<p class="text-sm">{row.label}</p>
						<p class="text-xs" style="color: var(--color-muted)">{row.hint}</p>
						{#if row.key === 'weeklyDigest'}
							<!-- Offered whether or not it is switched on: deciding whether to want a
							     weekly email is much easier having seen one. -->
							<button
								type="button"
								onclick={sendPreview}
								disabled={previewing}
								class="mt-1 text-xs underline underline-offset-2 disabled:opacity-60"
								style="color: var(--color-accent)"
							>
								{previewing ? 'Sending…' : 'Send me one now'}
							</button>
							{#if previewNote}
								<p class="mt-0.5 text-xs" style="color: var(--color-muted)">{previewNote}</p>
							{/if}
						{/if}
					</div>
				</li>
			{/each}
		</ul>
	{:else if !error}
		<SkeletonRows rows={4} class="mt-3" />
	{/if}
</div>
