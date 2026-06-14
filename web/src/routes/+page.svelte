<script lang="ts">
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();
</script>

<section class="mx-auto max-w-3xl">
	<h1 class="text-2xl font-semibold">Home</h1>
	<p class="mt-2" style="color: var(--color-muted)">
		You're signed in. Your playlists will appear here (B2).
	</p>

	<dl
		class="mt-6 grid grid-cols-[8rem_1fr] gap-y-2 rounded-lg border p-4 text-sm"
		style="border-color: var(--color-border); background: var(--color-surface)"
	>
		<dt style="color: var(--color-muted)">User ID</dt>
		<dd class="font-mono">{data.user?.userId}</dd>
		<dt style="color: var(--color-muted)">Auth method</dt>
		<dd>{data.user?.authMethod}</dd>
		<dt style="color: var(--color-muted)">Scopes</dt>
		<dd>{data.user?.scopes.length ? data.user.scopes.join(', ') : '(full access)'}</dd>
	</dl>
</section>
