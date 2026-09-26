<script lang="ts">
	import { onMount } from 'svelte';
	import { resolve } from '$app/paths';
	import { requireSession } from '$lib/auth';
	import { binLabel, fetchBins, type WarehouseBin } from '$lib/inventory';
	import { binBarcode } from '$lib/barcode';
	import Barcode from '$lib/components/shared/Barcode.svelte';
	import PrintSheet from '$lib/components/print/PrintSheet.svelte';

	// Gates the markup below, same as /bins: the warehouse map is open to anyone
	// signed in, so this only requires a session.
	let allowed = $state(false);

	let bins: WarehouseBin[] = $state([]);
	let isLoading = $state(true);
	let errorMsg = $state('');

	// The Bins page links here two ways: the header button with no filter, meaning
	// every label, and a row's printer button with ?bins=<id>, meaning the one bin
	// whose label has come off the shelf. Read as a list so a future multi-select
	// needs nothing here. Null - the parameter absent - is "all", which is not the
	// same as an empty selection.
	let requestedIds: number[] | null = $state(null);

	// The zone to print, chosen on screen. Restocking or re-labelling happens an
	// aisle at a time, and a warehouse's full label run is a lot of sheets to
	// throw away to get at one zone's worth.
	let zoneFilter = $state('');

	let zones = $derived([...new Set(bins.map((bin) => bin.zone))].sort());

	let selected = $derived(
		bins
			.filter((bin) => requestedIds === null || requestedIds.includes(bin.id))
			.filter((bin) => !zoneFilter || bin.zone === zoneFilter)
	);

	onMount(async () => {
		if (!requireSession()) return;
		allowed = true;

		// Read the same way /inventory reads its ?new=1. Anything that is not a
		// positive whole number is dropped rather than sent on as NaN: a mistyped
		// URL should print no label, not every label.
		const param = new URLSearchParams(window.location.search).get('bins');
		if (param !== null) {
			requestedIds = param
				.split(',')
				.map((part) => Number(part.trim()))
				.filter((id) => Number.isInteger(id) && id > 0);
		}

		try {
			bins = await fetchBins();
		} catch (err) {
			console.error(err);
			errorMsg = err instanceof Error ? err.message : 'Failed to load bins.';
		} finally {
			isLoading = false;
		}
	});
</script>

{#if allowed}
	<PrintSheet
		title="Bin Labels"
		description="Print on plain paper or a sheet of adhesive labels, then cut along the guides."
		backHref={resolve('/bins')}
		backLabel="Back to Bins"
	>
		{#snippet controls()}
			<div class="control">
				<label for="zone-filter">ZONE</label>
				<select id="zone-filter" bind:value={zoneFilter} disabled={zones.length === 0}>
					<option value="">All zones</option>
					{#each zones as zone (zone)}
						<option value={zone}>{zone}</option>
					{/each}
				</select>
			</div>
			<p class="count">{selected.length} label{selected.length === 1 ? '' : 's'}</p>
		{/snippet}

		{#if errorMsg}
			<p class="notice error">{errorMsg}</p>
		{:else if isLoading}
			<p class="notice">Loading bins...</p>
		{:else if selected.length === 0}
			<p class="notice">
				{bins.length === 0
					? 'No bins exist yet, so there is nothing to label.'
					: 'No bins match this selection.'}
			</p>
		{:else}
			<div class="label-grid">
				{#each selected as bin (bin.id)}
					<!-- One label. Reads top to bottom in the order it gets used: the
					     address a person walks to, the barcode a scanner reads, then the
					     small print that only matters when something has gone wrong. -->
					<div class="label">
						<p class="address">{binLabel(bin)}</p>
						<div class="barcode-slot">
							<!-- Wider and taller than the pick sheet's line codes. A shelf
							     label is read across an aisle, often from a forklift and often
							     at an angle, and the label has the room to spare. -->
							<Barcode
								value={binBarcode(bin.id)}
								moduleWidth={2.5}
								height={52}
								label="Bin {binLabel(bin)}"
							/>
						</div>
						<p class="token">{binBarcode(bin.id)}</p>
						<dl class="parts">
							<div>
								<dt>Zone</dt>
								<dd>{bin.zone}</dd>
							</div>
							<div>
								<dt>Aisle</dt>
								<dd>{bin.aisle}</dd>
							</div>
							<div>
								<dt>Shelf</dt>
								<dd>{bin.shelf}</dd>
							</div>
						</dl>
					</div>
				{/each}
			</div>
		{/if}
	</PrintSheet>
{/if}

<style>
	.control {
		display: flex;
		flex-direction: column;
		gap: 0.35rem;
	}
	.control label {
		font-size: 0.7rem;
		font-weight: 600;
		color: #475569;
		letter-spacing: 0.5px;
	}
	.control select {
		padding: 0.5rem 0.75rem;
		border: 1px solid #cbd5e1;
		border-radius: 6px;
		background: white;
		font-family: inherit;
	}
	.count {
		margin: 0 0 0.6rem 0;
		font-size: 0.85rem;
		color: #64748b;
		white-space: nowrap;
	}

	.notice {
		margin: 0;
		padding: 3rem 0;
		text-align: center;
		color: #64748b;
		font-style: italic;
	}
	.notice.error {
		color: #991b1b;
		font-style: normal;
	}

	/* Two across the 190mm the printer gives us, which leaves each label wide
	   enough that the barcode's quiet zones are never the thing that has to give.
	   A fixed height rather than one per label's content, so the rows line up
	   into something that can be cut with a guillotine in one pass. */
	.label-grid {
		display: grid;
		grid-template-columns: repeat(2, 1fr);
		gap: 4mm;
	}
	.label {
		box-sizing: border-box;
		height: 50mm;
		padding: 4mm;
		/* The cut guide. Dashed and grey so it reads as "cut here" rather than as
		   a border the label is supposed to have. */
		border: 1px dashed #94a3b8;
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		gap: 1.5mm;
		text-align: center;
		/* A label split down the middle by a page break is a wasted label. */
		break-inside: avoid;
	}
	.address {
		margin: 0;
		font-family: monospace;
		font-size: 20pt;
		font-weight: 700;
		letter-spacing: 1px;
		color: #000;
		/* A long zone name shrinks the address rather than pushing the barcode off
		   the bottom of the label. */
		overflow-wrap: anywhere;
		line-height: 1.1;
	}
	.barcode-slot {
		display: flex;
		justify-content: center;
	}
	.token {
		margin: 0;
		font-family: monospace;
		font-size: 8pt;
		letter-spacing: 1px;
		color: #000;
	}
	.parts {
		display: flex;
		gap: 4mm;
		margin: 0;
		font-size: 7pt;
		color: #000;
	}
	.parts div {
		display: flex;
		gap: 1mm;
	}
	.parts dt {
		text-transform: uppercase;
		letter-spacing: 0.5px;
		color: #475569;
	}
	.parts dd {
		margin: 0;
		font-weight: 700;
	}
</style>
