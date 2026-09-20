<script lang="ts">
	import favicon from '$lib/assets/favicon.svg';
	import { onMount } from 'svelte';
	import { dev } from '$app/environment';
	import { startAutoReplay } from '$lib/offlineQueue.svelte';
	import OfflineQueueBanner from '$lib/components/shared/OfflineQueueBanner.svelte';

	let { children } = $props();

	onMount(() => {
		// Registered from the root layout so it is in place before anyone signs
		// in, not just once inside the authenticated pages - a device that goes
		// into a dead zone before its first login should still get a shell that
		// opens (see src/service-worker.ts).
		if ('serviceWorker' in navigator) {
			navigator.serviceWorker.register('/service-worker.js', {
				// Vite serves the worker as an ES module in dev and bundles it to a
				// classic script for the production build - registering it with the
				// wrong type is a silent no-op in one of the two.
				type: dev ? 'module' : 'classic'
			});
		}

		return startAutoReplay();
	});
</script>

<svelte:head>
	<link rel="icon" href={favicon} />
</svelte:head>

<OfflineQueueBanner />

{@render children()}
