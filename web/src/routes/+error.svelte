<script lang="ts">
	import { pageTitle } from '$lib/title';
	import Page from '$lib/components/ui/Page.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import { page } from '$app/state';

	/**
	 * What a wrong turn looks like.
	 *
	 * There was no error page at all, so SvelteKit's fallback rendered a bare status and message
	 * with no title — the tab said "localhost:5173/admin" — and no way back to anywhere. Somebody
	 * who followed a stale link had the browser's back button and nothing else.
	 */
	const status = $derived(page.status);

	// Signing in from here should come back here, not start over from the home page.
	const signIn = $derived(
		`/login?redirectTo=${encodeURIComponent(page.url.pathname + page.url.search)}`
	);

	/** Said in terms of what happened to the person, not what happened to the server. */
	const heading = $derived.by(() => {
		if (status === 404) return 'There is nothing here';
		if (status === 403) return 'This is not yours to see';
		if (status === 401) return 'You need to be signed in';
		if (status === 429) return 'Too many requests just now';
		if (status >= 500) return 'Something went wrong at our end';
		return 'That did not work';
	});

	const detail = $derived.by(() => {
		if (status === 404) return 'The address may be wrong, or whatever was here has been removed.';
		if (status === 403) return 'Your account does not have access to this page.';
		if (status === 401) return 'Sign in and try again.';
		if (status === 429) return 'Wait a moment and try again.';
		if (status >= 500) return 'This is not your fault. Trying again shortly is worth a go.';
		return page.error?.message ?? 'Try again, or go back to your playlists.';
	});
</script>

<svelte:head><title>{pageTitle(`${heading}`)}</title></svelte:head>

<Page width="card" class="py-16 text-center">
	<p class="text-5xl font-semibold" style="color: var(--color-muted)">{status}</p>
	<h1 class="mt-3 text-xl font-semibold">{heading}</h1>
	<p class="mt-2 text-sm" style="color: var(--color-muted)">{detail}</p>

	<!-- Anywhere is better than a dead end — but somewhere the visitor can actually go. A
	     signed-out visitor offered "Your playlists" was only bounced to the sign-in page. -->
	<div class="mt-6 flex flex-wrap justify-center gap-3 text-sm">
		{#if status === 401}
			<Button href={signIn} variant="primary">Sign in</Button>
		{:else if page.data.user}
			<Button href="/playlists" variant="primary">Your playlists</Button>
		{:else}
			<Button href="/discover" variant="primary">Discover playlists</Button>
		{/if}
		<Button href="/">Home</Button>
	</div>
</Page>
