<script lang="ts">
	import {
		queuedMovements,
		pendingCount,
		conflictCount,
		removeQueueItem,
		retryQueueItem
	} from '$lib/offlineQueue.svelte';

	// Rendered from the root layout, above every page including the login
	// screen - "it syncs when you walk back" only holds if this is visible no
	// matter where in the app that walk-back happens.
	let items = $derived(queuedMovements());
	let pending = $derived(pendingCount());
	let conflicts = $derived(conflictCount());

	let retryingId: string | null = $state(null);

	async function retry(id: string) {
		retryingId = id;
		try {
			await retryQueueItem(id);
		} finally {
			retryingId = null;
		}
	}
</script>

{#if items.length > 0}
	<div class="offline-banner" class:has-conflicts={conflicts > 0}>
		<div class="summary">
			{#if conflicts > 0}
				<span class="icon">⚠️</span>
				<span>
					{conflicts} movement{conflicts === 1 ? '' : 's'} could not be resent automatically - review
					below.
					{#if pending > 0}Also waiting to sync: {pending}.{/if}
				</span>
			{:else}
				<span class="icon">☁️</span>
				<span
					>{pending} movement{pending === 1 ? '' : 's'} saved offline - will sync automatically.</span
				>
			{/if}
		</div>

		{#if conflicts > 0}
			<ul class="items">
				{#each items.filter((item) => item.status === 'conflict') as item (item.id)}
					<li>
						<div class="item-text">
							<strong>{item.description}</strong>
							<span class="conflict-message">{item.conflictMessage}</span>
						</div>
						<div class="item-actions">
							<button
								class="btn-outline"
								onclick={() => retry(item.id)}
								disabled={retryingId === item.id}
							>
								{retryingId === item.id ? 'Retrying...' : 'Retry'}
							</button>
							<button class="btn-outline danger" onclick={() => removeQueueItem(item.id)}>
								Discard
							</button>
						</div>
					</li>
				{/each}
			</ul>
		{/if}
	</div>
{/if}

<style>
	.offline-banner {
		background: #e0f2fe;
		border-bottom: 1px solid #7dd3fc;
		padding: 0.75rem 2rem;
		font-size: 0.85rem;
		color: #0369a1;
	}
	.offline-banner.has-conflicts {
		background: #fef3c7;
		border-bottom-color: #fbbf24;
		color: #92400e;
	}
	.summary {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		font-weight: 600;
	}
	.items {
		list-style: none;
		margin: 0.75rem 0 0 0;
		padding: 0;
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}
	.items li {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 1rem;
		background: white;
		border: 1px solid #fbbf24;
		border-radius: 6px;
		padding: 0.6rem 0.9rem;
	}
	.item-text {
		display: flex;
		flex-direction: column;
		gap: 0.15rem;
		font-weight: 400;
	}
	.item-text strong {
		color: #0f172a;
	}
	.conflict-message {
		font-size: 0.8rem;
		color: #92400e;
	}
	.item-actions {
		display: flex;
		gap: 0.5rem;
		flex-shrink: 0;
	}
	.btn-outline {
		background: white;
		color: #475569;
		border: 1px solid #cbd5e1;
		padding: 0.4rem 0.8rem;
		border-radius: 6px;
		font-weight: 600;
		font-size: 0.8rem;
		cursor: pointer;
	}
	.btn-outline:hover:not(:disabled) {
		background: #f1f5f9;
	}
	.btn-outline:disabled {
		cursor: not-allowed;
		opacity: 0.6;
	}
	.btn-outline.danger {
		color: #991b1b;
		border-color: #f87171;
	}
	.btn-outline.danger:hover {
		background: #fee2e2;
	}
</style>
