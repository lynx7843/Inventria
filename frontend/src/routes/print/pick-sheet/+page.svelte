<script lang="ts">
	import { onMount } from 'svelte';
	import { resolve } from '$app/paths';
	import { requireSession } from '$lib/auth';
	import { fetchPickList, lineBinLabel, type PickListDetail } from '$lib/pickLists';
	import { pickLineBarcode, pickListBarcode } from '$lib/barcode';
	import Barcode from '$lib/components/shared/Barcode.svelte';
	import PrintSheet from '$lib/components/print/PrintSheet.svelte';

	// Gates the markup below, same as /pick-lists: picking is open to anyone
	// signed in, so this only requires a session.
	let allowed = $state(false);

	// Typed through $state's own generic rather than as an annotation on the
	// binding. With `let list: PickListDetail | null = $state(null)`, TypeScript's
	// flow analysis reaches the deriveds at the bottom of this block having seen
	// only the initial null - the assignment lives inside onMount's callback,
	// which has not run yet - and narrows `list` to null, leaving `list.lines`
	// an error on a type that is by then `never`.
	let list = $state<PickListDetail | null>(null);
	let isLoading = $state(true);
	let errorMsg = $state('');

	// Which list to print, from ?list=<id> - read the same way /inventory reads
	// its ?new=1. The Pick Lists page is what normally supplies it.
	let listId: number | null = $state(null);

	onMount(async () => {
		if (!requireSession()) return;
		allowed = true;

		const param = new URLSearchParams(window.location.search).get('list');
		const parsed = Number(param);
		if (!Number.isInteger(parsed) || parsed <= 0) {
			errorMsg =
				'No pick list was named. Open one from the Pick Lists page and print it from there.';
			isLoading = false;
			return;
		}

		listId = parsed;

		try {
			list = await fetchPickList(parsed);
		} catch (err) {
			console.error(err);
			errorMsg = err instanceof Error ? err.message : 'Failed to load the pick list.';
		} finally {
			isLoading = false;
		}
	});

	function formatDateTime(iso: string | null): string {
		return iso ? new Date(iso).toLocaleString() : '—';
	}

	// The sheet is printed to be walked, so the count that matters on it is what
	// is still owed, not what the list originally asked for. A sheet reprinted
	// halfway through a pick is then a sheet for the half that is left.
	let remainingLines = $derived(list ? list.lines.filter((l) => l.quantityRemaining > 0) : []);
	let unitsRemaining = $derived(
		remainingLines.reduce((total, line) => total + line.quantityRemaining, 0)
	);
</script>

{#if allowed}
	<PrintSheet
		title={list ? `Pick Sheet #${list.id}` : 'Pick Sheet'}
		description="Walk the lines in order - they are already sorted into a route through the warehouse."
		backHref={listId ? resolve(`/pick-lists?list=${listId}`) : resolve('/pick-lists')}
		backLabel="Back to Pick Lists"
	>
		{#if errorMsg}
			<p class="notice error">{errorMsg}</p>
		{:else if isLoading}
			<p class="notice">Loading pick list...</p>
		{:else if list}
			<!-- The document header. The barcode here is the list itself, so a
			     picker holding the paper can scan it to pull the same list up on a
			     terminal instead of keying its number in. -->
			<header class="sheet-header">
				<div class="heading">
					<h2>Pick Sheet #{list.id}</h2>
					<dl class="meta">
						<div>
							<dt>Status</dt>
							<dd>{list.status}</dd>
						</div>
						<div>
							<dt>Opened</dt>
							<dd>{formatDateTime(list.createdAt)}</dd>
						</div>
						<div>
							<dt>Opened by</dt>
							<dd>{list.createdBy || '—'}</dd>
						</div>
						<div>
							<dt>To pick</dt>
							<dd>
								{remainingLines.length} line{remainingLines.length === 1 ? '' : 's'} · {unitsRemaining}
								unit{unitsRemaining === 1 ? '' : 's'}
							</dd>
						</div>
					</dl>
				</div>
				<div class="header-barcode">
					<Barcode
						value={pickListBarcode(list.id)}
						moduleWidth={2}
						height={38}
						label="Pick list {list.id}"
					/>
					<p class="token">{pickListBarcode(list.id)}</p>
				</div>
			</header>

			{#if list.lines.length === 0}
				<p class="notice">This pick list has no lines.</p>
			{:else}
				<table class="lines">
					<thead>
						<tr>
							<th class="col-seq">#</th>
							<th class="col-code">SCAN</th>
							<th class="col-bin">BIN</th>
							<th class="col-item">ITEM</th>
							<th class="col-qty">TO PICK</th>
							<th class="col-picked">PICKED</th>
						</tr>
					</thead>
					<tbody>
						{#each list.lines as line (line.id)}
							<tr class:done={line.quantityRemaining === 0}>
								<td class="col-seq">{line.sequence}</td>
								<td class="col-code">
									<Barcode
										value={pickLineBarcode(line.id)}
										moduleWidth={1.5}
										height={28}
										label="Pick line {line.sequence}"
									/>
									<span class="token">{pickLineBarcode(line.id)}</span>
								</td>
								<td class="col-bin"><span class="bin">{lineBinLabel(line)}</span></td>
								<td class="col-item">
									<span class="sku">{line.itemSku ?? `Item #${line.itemId}`}</span>
									<span class="name">{line.itemName ?? ''}</span>
									{#if line.lotNumber}
										<span class="lot">Lot {line.lotNumber}</span>
									{/if}
								</td>
								<td class="col-qty">
									{#if line.quantityRemaining === 0}
										<span class="qty-done">done</span>
									{:else}
										<span class="qty">{line.quantityRemaining}</span>
										{#if line.quantityPicked > 0}
											<span class="of">of {line.quantityRequested}</span>
										{/if}
									{/if}
								</td>
								<!-- Left blank on purpose: a picker writes the count they
								     actually found here, and short picks get keyed in from
								     the sheet afterwards. -->
								<td class="col-picked">{line.quantityRemaining === 0 ? '✓' : ''}</td>
							</tr>
						{/each}
					</tbody>
				</table>

				<footer class="sign-off">
					<div class="sign-line"><span>Picked by</span></div>
					<div class="sign-line"><span>Date</span></div>
				</footer>
			{/if}
		{/if}
	</PrintSheet>
{/if}

<style>
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

	.sheet-header {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		gap: 8mm;
		padding-bottom: 3mm;
		margin-bottom: 4mm;
		border-bottom: 2px solid #000;
	}
	.sheet-header h2 {
		margin: 0 0 2mm 0;
		font-size: 16pt;
		color: #000;
	}
	.meta {
		display: grid;
		grid-template-columns: repeat(2, auto);
		gap: 1mm 6mm;
		margin: 0;
		font-size: 9pt;
		color: #000;
	}
	.meta div {
		display: flex;
		gap: 2mm;
	}
	.meta dt {
		text-transform: uppercase;
		letter-spacing: 0.5px;
		color: #475569;
	}
	.meta dd {
		margin: 0;
		font-weight: 700;
	}
	.header-barcode {
		flex-shrink: 0;
		text-align: center;
	}
	.token {
		display: block;
		margin: 0;
		font-family: monospace;
		font-size: 7pt;
		letter-spacing: 1px;
		color: #000;
	}

	.lines {
		width: 100%;
		border-collapse: collapse;
		text-align: left;
	}
	.lines th {
		padding: 2mm 2mm 2mm 0;
		border-bottom: 1px solid #000;
		font-size: 7.5pt;
		letter-spacing: 0.5px;
		color: #475569;
		font-weight: 700;
	}
	.lines td {
		padding: 2mm 2mm 2mm 0;
		border-bottom: 1px solid #cbd5e1;
		font-size: 9pt;
		color: #000;
		vertical-align: middle;
	}
	/* A pick split across two sheets of paper is a pick someone walks twice. */
	.lines tr {
		break-inside: avoid;
	}
	/* A line already fulfilled stays on the sheet - it is a record of the list,
	   not only a set of instructions - but it should not compete for attention
	   with the ones still to walk. */
	.done td {
		color: #64748b;
	}

	.col-seq {
		width: 8mm;
		font-weight: 700;
	}
	.col-code {
		width: 44mm;
	}
	.col-bin {
		width: 34mm;
	}
	.col-qty {
		width: 20mm;
	}
	.col-picked {
		width: 22mm;
		/* The box a number gets written into by hand, so it has to be tall enough
		   to write in and obviously empty. */
		border-left: 1px solid #cbd5e1;
		padding-left: 2mm;
		height: 12mm;
		font-size: 12pt;
		font-weight: 700;
	}

	.bin {
		font-family: monospace;
		font-size: 11pt;
		font-weight: 700;
	}
	.col-item {
		display: table-cell;
	}
	.sku {
		display: block;
		font-family: monospace;
		font-weight: 700;
	}
	.name {
		display: block;
		font-size: 8pt;
	}
	.lot {
		display: block;
		font-size: 8pt;
		font-weight: 700;
	}
	.qty {
		font-size: 14pt;
		font-weight: 700;
	}
	.of {
		display: block;
		font-size: 7.5pt;
		color: #475569;
	}
	.qty-done {
		font-size: 9pt;
		font-style: italic;
	}

	.sign-off {
		display: flex;
		gap: 10mm;
		margin-top: 8mm;
		/* Follows the last line rather than being stranded on a page of its own. */
		break-inside: avoid;
	}
	.sign-line {
		flex: 1;
		border-top: 1px solid #000;
		padding-top: 1.5mm;
	}
	.sign-line span {
		font-size: 7.5pt;
		text-transform: uppercase;
		letter-spacing: 0.5px;
		color: #475569;
	}
</style>
