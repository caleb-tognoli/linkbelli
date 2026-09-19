<script lang="ts">
	import { Dialog } from 'bits-ui';
	import { api, json } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import {
		canRedeliver,
		describeDelivery,
		shortUrl,
		type Webhook,
		type WebhookDelivery,
		type WebhookEventInfo,
		type WebhookWithSecret
	} from '$lib/webhooks';
	import {
		Check,
		CircleAlert,
		Copy,
		History,
		KeyRound,
		Plus,
		RotateCcw,
		Send,
		Trash2,
		X
	} from '@lucide/svelte';
	import Switch from './Switch.svelte';

	let hooks = $state<Webhook[]>([]);
	let catalogue = $state<WebhookEventInfo[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);

	// The secret, for the one moment it can be seen: straight after a webhook is made or its
	// secret replaced. Like an API key, there is no way to read it back.
	let revealed = $state<{ hookId: string; secret: string } | null>(null);
	let copied = $state(false);

	let dialogOpen = $state(false);
	let url = $state('');
	let description = $state('');
	let chosen = $state<Set<string>>(new Set());
	let busy = $state(false);
	let formError = $state<string | null>(null);

	/** Which webhook's delivery log is open, and what it holds. */
	let openLog = $state<string | null>(null);
	let log = $state<WebhookDelivery[]>([]);
	let logLoading = $state(false);

	async function refresh() {
		try {
			const [list, events] = await Promise.all([
				json<Webhook[]>(await api.get('/me/webhooks')),
				json<WebhookEventInfo[]>(await api.get('/me/webhooks/events'))
			]);
			hooks = list;
			catalogue = events;
			error = null;
		} catch {
			error = 'Could not load your webhooks.';
		} finally {
			loading = false;
		}
	}

	$effect(() => {
		refresh();
	});

	function resetForm() {
		url = '';
		description = '';
		chosen = new Set(['items.added']);
		formError = null;
	}

	function toggle(event: string, on: boolean) {
		const next = new Set(chosen);
		if (on) next.add(event);
		else next.delete(event);
		chosen = next;
	}

	/** The API's own words for what was wrong, which are written to be shown. */
	async function problem(res: Response, fallback: string): Promise<string> {
		const body = await res.json().catch(() => null);
		const first = body?.errors ? Object.values(body.errors as Record<string, string[]>)[0]?.[0] : null;
		return first ?? body?.detail ?? fallback;
	}

	async function create() {
		if (busy || !url.trim() || chosen.size === 0) return;

		busy = true;
		formError = null;
		try {
			const res = await api.post('/me/webhooks', {
				url: url.trim(),
				events: [...chosen],
				description: description.trim() || null
			});
			if (!res.ok) {
				formError = await problem(res, 'Could not add that webhook.');
				return;
			}

			const created = (await res.json()) as WebhookWithSecret;
			hooks = [...hooks, created.webhook];
			revealed = { hookId: created.webhook.id, secret: created.secret };
			dialogOpen = false;
			resetForm();
		} finally {
			busy = false;
		}
	}

	async function setActive(hook: Webhook, active: boolean) {
		const res = await api.patch(`/me/webhooks/${hook.id}`, { active });
		if (res.ok) {
			const updated = (await res.json()) as Webhook;
			hooks = hooks.map((h) => (h.id === hook.id ? updated : h));
		} else {
			error = 'Could not change that webhook.';
		}
	}

	async function test(hook: Webhook) {
		const res = await api.post(`/me/webhooks/${hook.id}/test`);
		if (!res.ok) {
			error = res.status === 429 ? 'Too many tests at once — wait a minute.' : 'Could not send a test.';
			return;
		}

		// Straight to the log, which is where the answer will turn up.
		await showLog(hook, true);
	}

	async function rotate(hook: Webhook) {
		const ok = await confirmDialog(
			'Replace the signing secret? Everything sent from now on is signed with the new one, so update the receiver first or it will start rejecting deliveries.',
			{ confirmLabel: 'Replace' }
		);
		if (!ok) return;

		const res = await api.post(`/me/webhooks/${hook.id}/secret`);
		if (res.ok) {
			const rotated = (await res.json()) as WebhookWithSecret;
			revealed = { hookId: hook.id, secret: rotated.secret };
		} else {
			error = 'Could not replace the secret.';
		}
	}

	async function remove(hook: Webhook) {
		const ok = await confirmDialog(`Remove the webhook to ${shortUrl(hook.url)}? Nothing more will be sent to it.`, {
			danger: true,
			confirmLabel: 'Remove'
		});
		if (!ok) return;

		const res = await api.del(`/me/webhooks/${hook.id}`);
		if (res.ok) {
			hooks = hooks.filter((h) => h.id !== hook.id);
			if (openLog === hook.id) openLog = null;
			if (revealed?.hookId === hook.id) revealed = null;
		} else {
			error = 'Could not remove that webhook.';
		}
	}

	async function showLog(hook: Webhook, keepOpen = false) {
		if (openLog === hook.id && !keepOpen) {
			openLog = null;
			return;
		}

		openLog = hook.id;
		logLoading = true;
		try {
			log = await json<WebhookDelivery[]>(await api.get(`/me/webhooks/${hook.id}/deliveries`));
		} catch {
			log = [];
			error = 'Could not load the deliveries.';
		} finally {
			logLoading = false;
		}
	}

	async function redeliver(hook: Webhook, delivery: WebhookDelivery) {
		const res = await api.post(`/me/webhooks/deliveries/${delivery.id}/redeliver`);
		if (res.ok) {
			await showLog(hook, true);
		} else {
			error = res.status === 429 ? 'Too many at once — wait a minute.' : 'Could not send it again.';
		}
	}

	async function copySecret() {
		if (!revealed) return;
		try {
			await navigator.clipboard.writeText(revealed.secret);
			copied = true;
			setTimeout(() => (copied = false), 2000);
		} catch {
			/* clipboard blocked — the secret is still on screen to select */
		}
	}

	function when(iso: string | null): string {
		return iso
			? new Date(iso).toLocaleString(undefined, {
					month: 'short',
					day: 'numeric',
					hour: '2-digit',
					minute: '2-digit'
				})
			: 'never';
	}

	resetForm();
</script>

<div class="flex flex-col gap-3">
	<div class="flex items-center justify-between">
		<h2 class="font-medium">Webhooks</h2>
		<Dialog.Root
			bind:open={dialogOpen}
			onOpenChange={(o) => {
				if (!o) resetForm();
			}}
		>
			<Dialog.Trigger
				class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
				title="New webhook"
				aria-label="New webhook"
			>
				<Plus size={18} aria-hidden="true" />
			</Dialog.Trigger>

			<Dialog.Portal>
				<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
				<Dialog.Content
					class="fixed left-1/2 top-1/2 z-50 max-h-[90vh] w-[90vw] max-w-lg -translate-x-1/2 -translate-y-1/2 overflow-y-auto rounded-xl border p-6 shadow-xl"
					style="border-color: var(--color-border); background: var(--color-surface)"
				>
					<Dialog.Title class="text-lg font-semibold">New webhook</Dialog.Title>

					<div class="mt-4 flex flex-col gap-4">
						<label class="flex flex-col gap-1 text-sm">
							<span>Address to send to</span>
							<input
								bind:value={url}
								type="url"
								placeholder="https://…"
								class="rounded-md border px-3 py-2 text-sm"
								style="border-color: var(--color-border); background: var(--color-bg)"
							/>
						</label>

						<label class="flex flex-col gap-1 text-sm">
							<span>What it is for <span style="color: var(--color-muted)">(optional)</span></span>
							<input
								bind:value={description}
								maxlength="200"
								placeholder="Kitchen display, Discord #reading…"
								class="rounded-md border px-3 py-2 text-sm"
								style="border-color: var(--color-border); background: var(--color-bg)"
							/>
						</label>

						<fieldset class="flex flex-col gap-2.5 text-sm">
							<legend class="mb-1">Send it</legend>
							{#each catalogue as event (event.name)}
								<div class="flex items-start gap-2">
									<Switch
										checked={chosen.has(event.name)}
										onchange={(v) => toggle(event.name, v)}
										label={event.name}
									/>
									<div>
										<code>{event.name}</code>
										<p class="text-xs" style="color: var(--color-muted)">{event.description}</p>
									</div>
								</div>
							{/each}
						</fieldset>

						{#if formError}
							<p class="text-sm" style="color: var(--color-danger)">{formError}</p>
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
								disabled={busy || !url.trim() || chosen.size === 0}
								class="inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 font-medium disabled:opacity-60"
								style="background: var(--color-accent-solid); color: var(--color-on-solid)"
							>
								<Check size={15} aria-hidden="true" />
								{busy ? 'Adding…' : 'Add'}
							</button>
						</div>
					</div>
				</Dialog.Content>
			</Dialog.Portal>
		</Dialog.Root>
	</div>

	<p class="max-w-prose text-sm" style="color: var(--color-muted)">
		Tell something else what happens here — a chat channel, a home automation, a site that
		rebuilds. Each delivery is a signed JSON <code>POST</code>; check the
		<code>Linkbelli-Signature</code> header against the secret before trusting it.
	</p>

	{#if revealed}
		<div class="rounded-md border p-3 text-sm" style="border-color: var(--color-accent)">
			<p class="font-medium">Copy the signing secret now — it won't be shown again:</p>
			<code class="mt-2 block break-all rounded p-2" style="background: var(--color-surface)">{revealed.secret}</code>
			<div class="mt-2 flex items-center gap-3">
				<button
					type="button"
					class="inline-flex items-center rounded-md p-1.5"
					style="background: var(--color-accent-solid); color: var(--color-on-solid)"
					onclick={copySecret}
					title={copied ? 'Copied!' : 'Copy secret'}
					aria-label={copied ? 'Copied!' : 'Copy secret'}
				>
					{#if copied}
						<Check size={15} aria-hidden="true" />
					{:else}
						<Copy size={15} aria-hidden="true" />
					{/if}
				</button>
				<button
					type="button"
					onclick={() => (revealed = null)}
					title="Dismiss"
					aria-label="Dismiss"
					class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
				>
					<X size={15} aria-hidden="true" />
				</button>
			</div>
		</div>
	{/if}

	{#if error}
		<p class="text-sm" style="color: var(--color-danger)">{error}</p>
	{/if}

	{#if loading}
		<p class="text-sm" style="color: var(--color-muted)">Looking…</p>
	{:else if hooks.length === 0}
		<p class="text-sm" style="color: var(--color-muted)">No webhooks yet.</p>
	{:else}
		<ul class="flex flex-col gap-2">
			{#each hooks as hook (hook.id)}
				<li
					class="rounded-lg border px-3 py-2.5 text-sm"
					style="border-color: var(--color-border); background: var(--color-surface)"
				>
					<div class="flex items-center gap-3">
						<Switch
							checked={hook.status === 'Active'}
							onchange={(v) => setActive(hook, v)}
							label={hook.status === 'Active' ? 'Pause this webhook' : 'Turn this webhook on'}
						/>
						<div class="min-w-0 flex-1">
							<div class="truncate font-medium" title={hook.url}>
								{hook.description ?? shortUrl(hook.url)}
							</div>
							<div class="truncate text-xs" style="color: var(--color-muted)">
								{#if hook.description}{shortUrl(hook.url)} ·{/if}
								{hook.events.join(', ')} · last delivered {when(hook.lastDeliveredAt)}
							</div>
						</div>
						<button
							type="button"
							onclick={() => test(hook)}
							title="Send a test"
							aria-label="Send a test"
							class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
						>
							<Send size={16} aria-hidden="true" />
						</button>
						<button
							type="button"
							onclick={() => showLog(hook)}
							title="Recent deliveries"
							aria-label="Recent deliveries"
							aria-expanded={openLog === hook.id}
							class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
						>
							<History size={16} aria-hidden="true" />
						</button>
						<button
							type="button"
							onclick={() => rotate(hook)}
							title="Replace the signing secret"
							aria-label="Replace the signing secret"
							class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
						>
							<KeyRound size={16} aria-hidden="true" />
						</button>
						<button
							type="button"
							onclick={() => remove(hook)}
							title="Remove this webhook"
							aria-label="Remove this webhook"
							class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
							style="color: var(--color-danger)"
						>
							<Trash2 size={16} aria-hidden="true" />
						</button>
					</div>

					{#if hook.status === 'Disabled'}
						<!-- Said on the row, not left for the owner to infer from silence: this is the
						     state where a receiver stopped working and nothing else announces it. -->
						<p class="mt-2 flex items-start gap-1.5 text-xs" style="color: var(--color-warning)">
							<CircleAlert size={14} class="mt-px shrink-0" aria-hidden="true" />
							<span>
								Turned off by Linkbelli. {hook.disabledReason ?? ''} Send a test to check the receiver,
								then switch it back on.
							</span>
						</p>
					{:else if hook.consecutiveFailures > 0}
						<p class="mt-2 text-xs" style="color: var(--color-warning)">
							The last {hook.consecutiveFailures === 1 ? 'delivery' : `${hook.consecutiveFailures} deliveries`}
							could not be delivered.
						</p>
					{/if}

					{#if openLog === hook.id}
						<div class="mt-3 border-t pt-2" style="border-color: var(--color-border)">
							{#if logLoading}
								<p class="text-xs" style="color: var(--color-muted)">Looking…</p>
							{:else if log.length === 0}
								<p class="text-xs" style="color: var(--color-muted)">Nothing sent yet.</p>
							{:else}
								<ul class="flex flex-col gap-1.5 text-xs">
									{#each log as d (d.id)}
										<li class="flex items-start gap-2">
											<code class="shrink-0">{d.event}</code>
											<div class="min-w-0 flex-1">
												<span
													style="color: {d.status === 'Delivered'
														? 'var(--color-success)'
														: d.status === 'Failed' || d.status === 'Retrying'
															? 'var(--color-danger)'
															: 'var(--color-muted)'}"
												>
													{describeDelivery(d)}
												</span>
												<span style="color: var(--color-muted)">· {when(d.createdAt)}</span>
												{#if d.error}
													<p class="break-words" style="color: var(--color-muted)">{d.error}</p>
												{/if}
											</div>
											{#if canRedeliver(d)}
												<button
													type="button"
													onclick={() => redeliver(hook, d)}
													title="Send it again"
													aria-label="Send it again"
													class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
												>
													<RotateCcw size={13} aria-hidden="true" />
												</button>
											{/if}
										</li>
									{/each}
								</ul>
							{/if}
						</div>
					{/if}
				</li>
			{/each}
		</ul>
	{/if}
</div>
