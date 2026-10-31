<script lang="ts">
	// Every item's stock level over time, one line each, with a range picker.
	// Fetches its own data rather than riding on the dashboard totals call: it is
	// the same chart on both dashboards, and changing the range re-asks for this
	// alone.
	import { onMount } from 'svelte';
	import LineChart from './LineChart.svelte';
	import { fetchStockHistory, HISTORY_RANGES, type StockHistory } from '$lib/stockHistory';

	let days: number = $state(30);
	let history: StockHistory = $state({ dates: [], series: [] });
	let loading = $state(true);
	let error = $state('');

	// Only the newest request may land: a slow 90-day answer arriving after a
	// quick 7-day one would otherwise put the wrong range under the 7D button.
	let latest = 0;

	async function load(range: number) {
		const request = ++latest;
		loading = true;
		error = '';

		try {
			const result = await fetchStockHistory(range);
			if (request === latest) history = result;
		} catch (err) {
			if (request === latest) error = err instanceof Error ? err.message : 'Failed to load.';
		} finally {
			if (request === latest) loading = false;
		}
	}

	function pick(range: number) {
		if (range === days) return;
		days = range;
		load(range);
	}

	onMount(() => load(days));

	// The API sends warehouse days as bare dates. Read as UTC and printed as UTC
	// so a viewer west of Greenwich does not see every label a day early.
	const dateLabels = $derived(
		history.dates.map((d) =>
			new Date(`${d}T00:00:00Z`).toLocaleDateString('en-US', {
				month: 'short',
				day: 'numeric',
				timeZone: 'UTC'
			})
		)
	);

	const series = $derived(
		history.series.map((s) => ({ id: s.itemId, label: s.name, values: s.levels }))
	);
</script>

<LineChart
	title="Stock Level History"
	xLabels={dateLabels}
	{series}
	{loading}
	{error}
	emptyText="No items in the catalogue yet."
>
	{#snippet controls()}
		<div class="ranges" role="group" aria-label="Time range">
			{#each HISTORY_RANGES as range (range)}
				<button
					type="button"
					class:active={days === range}
					aria-pressed={days === range}
					onclick={() => pick(range)}>{range}D</button
				>
			{/each}
		</div>
	{/snippet}
</LineChart>

<style>
	.ranges {
		display: inline-flex;
		background: #f1f5f9;
		border-radius: 6px;
		padding: 2px;
		flex-shrink: 0;
	}
	.ranges button {
		background: none;
		border: none;
		padding: 0.25rem 0.6rem;
		border-radius: 4px;
		font: inherit;
		font-size: 0.75rem;
		font-weight: 600;
		color: #64748b;
		cursor: pointer;
	}
	.ranges button.active {
		background: white;
		color: #0f172a;
		box-shadow: 0 1px 2px rgb(0 0 0 / 8%);
	}
</style>
