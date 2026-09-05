<script lang="ts">
	import Sidebar from '$lib/components/shared/Sidebar.svelte';
	import Header from '$lib/components/shared/Header.svelte';
	import SelectField from '$lib/components/shared/SelectField.svelte';
	import { onMount } from 'svelte';
	import { requireSession, getUsername } from '$lib/auth';
	import { fetchAllItems, fetchBins, itemLabel, type Option } from '$lib/inventory';
	import {
		fetchStockOnHand,
		fetchMovements,
		fetchDeadStock,
		fetchVelocity,
		fetchReorder,
		type StockOnHandRow,
		type MovementRow,
		type DeadStockRow,
		type VelocityRow,
		type ReorderRow
	} from '$lib/reports';

	// Open to anyone signed in, same as Inventory and Bins - reports on the
	// catalogue aren't an Admin secret.
	let allowed = $state(false);

	type Tab = 'stock' | 'movements' | 'dead-stock' | 'velocity' | 'reorder' | 'purchase-order';
	let activeTab: Tab = $state('stock');

	// The tab strip as data rather than six hand-written buttons, so that the
	// ?tab= link the dashboards send people here with can be checked against the
	// same list the strip renders from.
	const TABS: { id: Tab; label: string }[] = [
		{ id: 'stock', label: 'Stock on Hand' },
		{ id: 'movements', label: 'Movements' },
		{ id: 'reorder', label: 'Reorder' },
		{ id: 'purchase-order', label: 'Purchase Order' },
		{ id: 'dead-stock', label: 'Dead Stock' },
		{ id: 'velocity', label: 'Velocity' }
	];

	const PAGE_SIZE = 15;

	// What the dropdown filters offer. Built once from the catalogue and the
	// warehouse map rather than from whatever page of a report happens to have
	// loaded, so a category or bin with nothing currently on hand still shows
	// up as something you can filter by.
	type Choice = { value: string; label: string };
	let categoryOptions: Choice[] = $state([]);
	let zoneOptions: Choice[] = $state([]);
	let itemOptions: Option[] = $state([]);
	let isLoadingOptions = $state(true);

	async function loadFilterOptions() {
		try {
			const [items, bins] = await Promise.all([fetchAllItems(), fetchBins()]);
			categoryOptions = Array.from(new Set(items.map((i) => i.category)))
				.sort()
				.map((c) => ({ value: c, label: c }));
			zoneOptions = Array.from(new Set(bins.map((b) => b.zone)))
				.sort()
				.map((z) => ({ value: z, label: z }));
			itemOptions = items.map((item) => ({ value: item.id, label: itemLabel(item) }));
		} catch {
			// The report tables below still work with free-typed filters absent -
			// only these three dropdowns come up empty, and each degrades to its
			// "no options yet" label rather than blocking the page.
		} finally {
			isLoadingOptions = false;
		}
	}

	// --- STOCK ON HAND -------------------------------------------------------

	let stockCategory = $state('');
	let stockZone = $state('');
	let stockPage = $state(1);
	let stockRows: StockOnHandRow[] = $state([]);
	let stockTotalPages = $state(1);
	let stockTotalCount = $state(0);
	let stockTotalUnits = $state(0);
	let stockLoading = $state(true);
	let stockError = $state('');

	async function loadStockOnHand() {
		stockLoading = true;
		stockError = '';
		try {
			const result = await fetchStockOnHand({
				category: stockCategory,
				zone: stockZone,
				page: stockPage,
				pageSize: PAGE_SIZE
			});
			stockRows = result.items;
			stockTotalPages = result.totalPages;
			stockTotalCount = result.totalCount;
			stockTotalUnits = result.totalUnits;
		} catch (err) {
			stockError = err instanceof Error ? err.message : 'Failed to load stock on hand.';
		} finally {
			stockLoading = false;
		}
	}

	function applyStockFilters(e: Event) {
		e.preventDefault();
		stockPage = 1;
		loadStockOnHand();
	}

	function goToStockPage(next: number) {
		if (next < 1 || next > stockTotalPages || next === stockPage) return;
		stockPage = next;
		loadStockOnHand();
	}

	// --- MOVEMENTS -------------------------------------------------------------

	let movementType = $state('');
	let movementItemId = $state('');
	let movementPerformedBy = $state('');
	let movementFrom = $state('');
	let movementTo = $state('');
	let movementPage = $state(1);
	let movementRows: MovementRow[] = $state([]);
	let movementTotalPages = $state(1);
	let movementTotalCount = $state(0);
	let movementLoading = $state(true);
	let movementError = $state('');

	async function loadMovements() {
		movementLoading = true;
		movementError = '';
		try {
			const result = await fetchMovements({
				type: movementType,
				itemId: movementItemId ? Number(movementItemId) : undefined,
				performedBy: movementPerformedBy,
				from: movementFrom,
				to: movementTo,
				page: movementPage,
				pageSize: PAGE_SIZE
			});
			movementRows = result.items;
			movementTotalPages = result.totalPages;
			movementTotalCount = result.totalCount;
		} catch (err) {
			movementError = err instanceof Error ? err.message : 'Failed to load movements.';
		} finally {
			movementLoading = false;
		}
	}

	function applyMovementFilters(e: Event) {
		e.preventDefault();
		movementPage = 1;
		loadMovements();
	}

	function goToMovementPage(next: number) {
		if (next < 1 || next > movementTotalPages || next === movementPage) return;
		movementPage = next;
		loadMovements();
	}

	// --- DEAD STOCK --------------------------------------------------------------

	let deadStockDays = $state('90');
	let deadStockPage = $state(1);
	let deadStockRows: DeadStockRow[] = $state([]);
	let deadStockTotalPages = $state(1);
	let deadStockTotalCount = $state(0);
	let deadStockLoading = $state(true);
	let deadStockError = $state('');

	async function loadDeadStock() {
		deadStockLoading = true;
		deadStockError = '';
		try {
			const days = Number(deadStockDays);
			const result = await fetchDeadStock({
				days: Number.isInteger(days) && days > 0 ? days : 90,
				page: deadStockPage,
				pageSize: PAGE_SIZE
			});
			deadStockRows = result.items;
			deadStockTotalPages = result.totalPages;
			deadStockTotalCount = result.totalCount;
			deadStockDays = String(result.days);
		} catch (err) {
			deadStockError = err instanceof Error ? err.message : 'Failed to load dead stock.';
		} finally {
			deadStockLoading = false;
		}
	}

	function applyDeadStockFilters(e: Event) {
		e.preventDefault();
		deadStockPage = 1;
		loadDeadStock();
	}

	function goToDeadStockPage(next: number) {
		if (next < 1 || next > deadStockTotalPages || next === deadStockPage) return;
		deadStockPage = next;
		loadDeadStock();
	}

	// --- VELOCITY ------------------------------------------------------------------

	let velocityDays = $state('30');
	let velocityPage = $state(1);
	let velocityRows: VelocityRow[] = $state([]);
	let velocityTotalPages = $state(1);
	let velocityTotalCount = $state(0);
	let velocityLoading = $state(true);
	let velocityError = $state('');

	async function loadVelocity() {
		velocityLoading = true;
		velocityError = '';
		try {
			const days = Number(velocityDays);
			const result = await fetchVelocity({
				days: Number.isInteger(days) && days > 0 ? days : 30,
				page: velocityPage,
				pageSize: PAGE_SIZE
			});
			velocityRows = result.items;
			velocityTotalPages = result.totalPages;
			velocityTotalCount = result.totalCount;
			velocityDays = String(result.days);
		} catch (err) {
			velocityError = err instanceof Error ? err.message : 'Failed to load velocity.';
		} finally {
			velocityLoading = false;
		}
	}

	function applyVelocityFilters(e: Event) {
		e.preventDefault();
		velocityPage = 1;
		loadVelocity();
	}

	function goToVelocityPage(next: number) {
		if (next < 1 || next > velocityTotalPages || next === velocityPage) return;
		velocityPage = next;
		loadVelocity();
	}

	// --- REORDER -------------------------------------------------------------

	let reorderCategory = $state('');
	let reorderPage = $state(1);
	let reorderRows: ReorderRow[] = $state([]);
	let reorderTotalPages = $state(1);
	let reorderTotalCount = $state(0);
	let reorderUnitsToOrder = $state(0);
	let reorderOrderableCount = $state(0);
	let reorderLoading = $state(true);
	let reorderError = $state('');

	async function loadReorder() {
		reorderLoading = true;
		reorderError = '';
		try {
			const result = await fetchReorder({
				category: reorderCategory,
				page: reorderPage,
				pageSize: PAGE_SIZE
			});
			reorderRows = result.items;
			reorderTotalPages = result.totalPages;
			reorderTotalCount = result.totalCount;
			reorderUnitsToOrder = result.totalUnitsToOrder;
			reorderOrderableCount = result.orderableCount;
		} catch (err) {
			reorderError = err instanceof Error ? err.message : 'Failed to load the reorder list.';
		} finally {
			reorderLoading = false;
		}
	}

	function applyReorderFilters(e: Event) {
		e.preventDefault();
		reorderPage = 1;
		loadReorder();
	}

	function goToReorderPage(next: number) {
		if (next < 1 || next > reorderTotalPages || next === reorderPage) return;
		reorderPage = next;
		loadReorder();
	}

	// --- SUGGESTED PURCHASE ORDER --------------------------------------------

	// The reorder report answers "what is low"; this answers "so what do I buy",
	// which is the same rows with the ones that carry no quantity left out and
	// nothing paged away. There is no separate endpoint because there is no
	// separate question - a purchase order is the reorder list written out as a
	// document.
	//
	// What the model does not have is a supplier or a unit cost, so this is a
	// list of what to buy and how much, not a priced order addressed to anyone.
	// Inventing either would be inventing the part a buyer is accountable for.
	const PO_MAX_LINES = 200;

	let poRows: ReorderRow[] = $state([]);
	let poTotalCount = $state(0);
	let poUnitsToOrder = $state(0);
	let poOrderableCount = $state(0);
	let poLoading = $state(false);
	let poError = $state('');
	let poLoaded = $state(false);
	let poGeneratedAt = $state('');

	async function loadPurchaseOrder() {
		poLoading = true;
		poError = '';
		try {
			// The whole list rather than a page of it: a purchase order that stops
			// at row 15 is not a purchase order. PO_MAX_LINES is the API's own
			// ceiling on a single request, and the note below says so when a
			// warehouse is somehow past it.
			const result = await fetchReorder({ page: 1, pageSize: PO_MAX_LINES });

			poRows = result.items.filter((row) => row.suggestedOrderQuantity > 0);
			poTotalCount = result.totalCount;
			poUnitsToOrder = result.totalUnitsToOrder;
			poOrderableCount = result.orderableCount;
			poGeneratedAt = new Date().toLocaleString();
			poLoaded = true;
		} catch (err) {
			poError = err instanceof Error ? err.message : 'Failed to build the purchase order.';
		} finally {
			poLoading = false;
		}
	}

	// Every other report on this page is one page of fifteen rows, which is why
	// they all load up front. This one asks for up to two hundred, so it waits
	// until someone actually opens it.
	function showTab(tab: Tab) {
		activeTab = tab;
		if (tab === 'purchase-order' && !poLoaded && !poLoading) loadPurchaseOrder();
	}

	/** How many low items the order cannot put a quantity against. See ReorderRow. */
	const poUnquantified = $derived(poTotalCount - poOrderableCount);

	/** Whether the warehouse has more low items than one request can carry. */
	const poTruncated = $derived(poTotalCount > PO_MAX_LINES);

	onMount(() => {
		if (!requireSession()) return;
		allowed = true;

		// All four reports load up front rather than one tab at a time: each is
		// one page of up to 15 rows, so the cost of loading all of them is the
		// cost of loading one - and switching tabs then never shows a spinner
		// for data that was already a click away.
		loadFilterOptions();
		loadStockOnHand();
		loadMovements();
		loadDeadStock();
		loadVelocity();
		loadReorder();

		// The dashboards' low-stock tile links straight at ?tab=reorder, so the
		// person who clicked a number lands on the list behind it rather than on
		// stock-on-hand with a tab still to find.
		const requested = new URLSearchParams(window.location.search).get('tab');
		if (TABS.some((tab) => tab.id === requested)) showTab(requested as Tab);
	});

	function formatTimestamp(iso: string): string {
		return new Date(iso).toLocaleString();
	}

	function formatDate(iso: string | null): string {
		return iso ? new Date(iso).toLocaleDateString() : 'Never';
	}

	function movementTypeClass(type: string): string {
		if (type === 'RECEIVE') return 'in';
		if (type === 'PICK') return 'out';
		return 'move';
	}

	// Exports what is currently on screen for the active tab - one page of a
	// report someone is already looking at, not the whole report re-fetched a
	// page at a time. The point is a file that matches what was just reviewed.
	function exportCsv() {
		let headers: string[];
		let rows: string[][];
		let filename: string;

		if (activeTab === 'stock') {
			headers = ['Item Name', 'SKU', 'Category', 'Zone', 'Aisle', 'Shelf', 'Quantity'];
			rows = stockRows.map((r) => [
				r.name,
				r.sku,
				r.category,
				r.zone,
				r.aisle,
				r.shelf,
				String(r.quantity)
			]);
			filename = 'stock-on-hand';
		} else if (activeTab === 'movements') {
			headers = ['Timestamp', 'Item', 'SKU', 'Type', 'Quantity Changed', 'Bin', 'Performed By'];
			rows = movementRows.map((r) => [
				formatTimestamp(r.timestamp),
				r.itemName,
				r.sku ?? '',
				r.transactionType,
				String(r.quantityChanged),
				r.zone ? `${r.zone}-${r.aisle}-${r.shelf}` : '',
				r.performedBy
			]);
			filename = 'stock-movements';
		} else if (activeTab === 'reorder') {
			headers = [
				'Item Name',
				'SKU',
				'Category',
				'Quantity On Hand',
				'Reorder Point',
				'Short By',
				'Reorder Quantity',
				'Suggested Order Quantity'
			];
			rows = reorderRows.map((r) => [
				r.name,
				r.sku,
				r.category,
				String(r.quantityOnHand),
				String(r.reorderPoint),
				String(r.shortfall),
				String(r.reorderQuantity),
				String(r.suggestedOrderQuantity)
			]);
			filename = 'reorder';
		} else if (activeTab === 'purchase-order') {
			headers = [
				'SKU',
				'Item Name',
				'Category',
				'Quantity On Hand',
				'Reorder Point',
				'Order Quantity'
			];
			rows = poRows.map((r) => [
				r.sku,
				r.name,
				r.category,
				String(r.quantityOnHand),
				String(r.reorderPoint),
				String(r.suggestedOrderQuantity)
			]);
			filename = 'purchase-order';
		} else if (activeTab === 'dead-stock') {
			headers = ['Item Name', 'SKU', 'Category', 'Quantity On Hand', 'Last Movement'];
			rows = deadStockRows.map((r) => [
				r.name,
				r.sku,
				r.category,
				String(r.quantityOnHand),
				formatDate(r.lastMovementAt)
			]);
			filename = 'dead-stock';
		} else {
			headers = ['Item Name', 'SKU', 'Category', 'Units In', 'Units Out', 'Net Change'];
			rows = velocityRows.map((r) => [
				r.name,
				r.sku,
				r.category,
				String(r.unitsIn),
				String(r.unitsOut),
				String(r.netChange)
			]);
			filename = 'velocity';
		}

		const csv = [headers, ...rows]
			.map((row) => row.map((cell) => `"${cell.replace(/"/g, '""')}"`).join(','))
			.join('\r\n');

		const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
		const link = document.createElement('a');

		link.href = url;
		link.download = `inventria-${filename}-${new Date().toISOString().slice(0, 10)}.csv`;
		link.click();

		URL.revokeObjectURL(url);
	}
</script>

{#if allowed}
	<Sidebar activePage="Reports" />
	<Header />

	<main class="dashboard-content">
		<div class="page-header">
			<div>
				<h2>Reports</h2>
				<p>Stock on hand, the movement ledger, dead stock, and reorder velocity.</p>
			</div>
			<div class="actions">
				<button class="btn-outline" onclick={exportCsv}>Export CSV</button>
				<button class="btn-solid" onclick={() => window.print()}>Print Report</button>
			</div>
		</div>

		<div class="tabs">
			{#each TABS as tab (tab.id)}
				<button class="tab" class:active={activeTab === tab.id} onclick={() => showTab(tab.id)}>
					{tab.label}
				</button>
			{/each}
		</div>

		<!-- STOCK ON HAND -->
		{#if activeTab === 'stock'}
			<div class="panel">
				<form class="filters" onsubmit={applyStockFilters}>
					<div class="select-wrap">
						<SelectField
							id="stock-category"
							label="CATEGORY"
							options={categoryOptions}
							bind:value={stockCategory}
							placeholder="All categories"
							emptyLabel={isLoadingOptions ? 'Loading...' : 'No categories yet'}
						/>
					</div>
					<div class="select-wrap">
						<SelectField
							id="stock-zone"
							label="ZONE"
							options={zoneOptions}
							bind:value={stockZone}
							placeholder="All zones"
							emptyLabel={isLoadingOptions ? 'Loading...' : 'No zones yet'}
						/>
					</div>
					<button type="submit" class="btn-solid filter-btn">Apply Filters</button>
					{#if stockCategory || stockZone}
						<button
							type="button"
							class="btn-outline filter-btn"
							onclick={() => {
								stockCategory = '';
								stockZone = '';
								stockPage = 1;
								loadStockOnHand();
							}}
						>
							Clear
						</button>
					{/if}
				</form>

				{#if !stockLoading && !stockError && stockTotalCount > 0}
					<p class="summary-line">
						{stockTotalCount} row{stockTotalCount === 1 ? '' : 's'} matching these filters, totalling
						{stockTotalUnits.toLocaleString()} units.
					</p>
				{/if}

				<table class="data-table">
					<thead>
						<tr>
							<th>ITEM NAME</th>
							<th>SKU</th>
							<th>CATEGORY</th>
							<th>BIN</th>
							<th>QUANTITY</th>
						</tr>
					</thead>
					<tbody>
						{#if stockLoading}
							<tr><td colspan="5" class="empty-state">Loading stock on hand...</td></tr>
						{:else if stockError}
							<tr><td colspan="5" class="empty-state error">{stockError}</td></tr>
						{:else if stockRows.length === 0}
							<tr><td colspan="5" class="empty-state">No stock matches these filters.</td></tr>
						{:else}
							{#each stockRows as row (row.itemId + '-' + row.warehouseBinId)}
								<tr>
									<td><strong>{row.name}</strong></td>
									<td>{row.sku}</td>
									<td>{row.category}</td>
									<td>{row.zone}-{row.aisle}-{row.shelf}</td>
									<td>{row.quantity.toLocaleString()}</td>
								</tr>
							{/each}
						{/if}
					</tbody>
				</table>

				{#if stockTotalCount > 0}
					<div class="pager">
						<span class="pager-status">
							Page {stockPage} of {stockTotalPages}
						</span>
						<div class="pager-buttons">
							<button
								class="pager-btn"
								onclick={() => goToStockPage(stockPage - 1)}
								disabled={stockPage <= 1 || stockLoading}>Previous</button
							>
							<button
								class="pager-btn"
								onclick={() => goToStockPage(stockPage + 1)}
								disabled={stockPage >= stockTotalPages || stockLoading}>Next</button
							>
						</div>
					</div>
				{/if}
			</div>
		{/if}

		<!-- MOVEMENTS -->
		{#if activeTab === 'movements'}
			<div class="panel">
				<form class="filters" onsubmit={applyMovementFilters}>
					<div class="select-wrap">
						<SelectField
							id="movement-type"
							label="TYPE"
							options={[
								{ value: 'RECEIVE', label: 'Receive' },
								{ value: 'PICK', label: 'Pick' },
								{ value: 'RELOCATE', label: 'Relocate' }
							]}
							bind:value={movementType}
							placeholder="All types"
						/>
					</div>
					<div class="select-wrap">
						<SelectField
							id="movement-item"
							label="ITEM"
							options={itemOptions}
							bind:value={movementItemId}
							placeholder="All items"
							emptyLabel={isLoadingOptions ? 'Loading...' : 'No items yet'}
						/>
					</div>
					<div class="input-wrap">
						<label for="movement-performed-by">PERFORMED BY</label>
						<input id="movement-performed-by" type="text" bind:value={movementPerformedBy} />
					</div>
					<div class="input-wrap">
						<label for="movement-from">FROM</label>
						<input id="movement-from" type="date" bind:value={movementFrom} />
					</div>
					<div class="input-wrap">
						<label for="movement-to">TO</label>
						<input id="movement-to" type="date" bind:value={movementTo} />
					</div>
					<button type="submit" class="btn-solid filter-btn">Apply Filters</button>
					{#if movementType || movementItemId || movementPerformedBy || movementFrom || movementTo}
						<button
							type="button"
							class="btn-outline filter-btn"
							onclick={() => {
								movementType = '';
								movementItemId = '';
								movementPerformedBy = '';
								movementFrom = '';
								movementTo = '';
								movementPage = 1;
								loadMovements();
							}}
						>
							Clear
						</button>
					{/if}
				</form>

				<table class="data-table">
					<thead>
						<tr>
							<th>TIMESTAMP</th>
							<th>ITEM</th>
							<th>TYPE</th>
							<th>QTY CHANGED</th>
							<th>BIN</th>
							<th>PERFORMED BY</th>
						</tr>
					</thead>
					<tbody>
						{#if movementLoading}
							<tr><td colspan="6" class="empty-state">Loading movements...</td></tr>
						{:else if movementError}
							<tr><td colspan="6" class="empty-state error">{movementError}</td></tr>
						{:else if movementRows.length === 0}
							<tr><td colspan="6" class="empty-state">No movements match these filters.</td></tr>
						{:else}
							{#each movementRows as row (row.id)}
								<tr>
									<td>{formatTimestamp(row.timestamp)}</td>
									<td>
										<strong>{row.itemName}</strong>{#if row.sku}<span class="sub">{row.sku}</span
											>{/if}
									</td>
									<td
										><span class="badge {movementTypeClass(row.transactionType)}"
											>{row.transactionType}</span
										></td
									>
									<td
										class:positive={row.quantityChanged > 0}
										class:negative={row.quantityChanged < 0}
									>
										{row.quantityChanged > 0 ? '+' : ''}{row.quantityChanged}
									</td>
									<td>{row.zone ? `${row.zone}-${row.aisle}-${row.shelf}` : '—'}</td>
									<td>{row.performedBy}</td>
								</tr>
							{/each}
						{/if}
					</tbody>
				</table>

				{#if movementTotalCount > 0}
					<div class="pager">
						<span class="pager-status">
							Page {movementPage} of {movementTotalPages}
						</span>
						<div class="pager-buttons">
							<button
								class="pager-btn"
								onclick={() => goToMovementPage(movementPage - 1)}
								disabled={movementPage <= 1 || movementLoading}>Previous</button
							>
							<button
								class="pager-btn"
								onclick={() => goToMovementPage(movementPage + 1)}
								disabled={movementPage >= movementTotalPages || movementLoading}>Next</button
							>
						</div>
					</div>
				{/if}
			</div>
		{/if}

		<!-- REORDER -->
		{#if activeTab === 'reorder'}
			<div class="panel">
				<form class="filters" onsubmit={applyReorderFilters}>
					<div class="select-wrap">
						<SelectField
							id="reorder-category"
							label="CATEGORY"
							options={categoryOptions}
							bind:value={reorderCategory}
							placeholder="All categories"
							emptyLabel={isLoadingOptions ? 'Loading...' : 'No categories yet'}
						/>
					</div>
					<button type="submit" class="btn-solid filter-btn">Apply Filters</button>
					{#if reorderCategory}
						<button
							type="button"
							class="btn-outline filter-btn"
							onclick={() => {
								reorderCategory = '';
								reorderPage = 1;
								loadReorder();
							}}
						>
							Clear
						</button>
					{/if}
				</form>

				<p class="summary-line">
					Items that have fallen to or below the level they say they need more at. An item with no
					reorder point set never appears here, however empty its shelf is.
				</p>

				{#if !reorderLoading && !reorderError && reorderTotalCount > 0}
					<p class="summary-line strong">
						{reorderTotalCount} item{reorderTotalCount === 1 ? '' : 's'} to reorder, totalling
						{reorderUnitsToOrder.toLocaleString()} units.
						{#if reorderOrderableCount < reorderTotalCount}
							{reorderTotalCount - reorderOrderableCount} of them carry no suggested amount, because they
							sit exactly on their reorder point with no reorder quantity recorded.
						{/if}
					</p>
				{/if}

				<table class="data-table">
					<thead>
						<tr>
							<th>ITEM NAME</th>
							<th>SKU</th>
							<th>CATEGORY</th>
							<th>ON HAND</th>
							<th>REORDER POINT</th>
							<th>SHORT BY</th>
							<th>SUGGESTED ORDER</th>
						</tr>
					</thead>
					<tbody>
						{#if reorderLoading}
							<tr><td colspan="7" class="empty-state">Loading the reorder list...</td></tr>
						{:else if reorderError}
							<tr><td colspan="7" class="empty-state error">{reorderError}</td></tr>
						{:else if reorderRows.length === 0}
							<tr>
								<td colspan="7" class="empty-state">
									Nothing needs reordering - every item with a reorder point is above it.
								</td>
							</tr>
						{:else}
							{#each reorderRows as row (row.itemId)}
								<tr>
									<td><strong>{row.name}</strong></td>
									<td>{row.sku}</td>
									<td>{row.category}</td>
									<td class:negative={row.quantityOnHand === 0}>
										{row.quantityOnHand.toLocaleString()}
									</td>
									<td>{row.reorderPoint.toLocaleString()}</td>
									<td>{row.shortfall.toLocaleString()}</td>
									<td>
										{#if row.suggestedOrderQuantity > 0}
											<strong>{row.suggestedOrderQuantity.toLocaleString()}</strong>
											{#if row.reorderQuantity > 0}
												<span class="sub">in lots of {row.reorderQuantity.toLocaleString()}</span>
											{/if}
										{:else}
											<span class="sub">No reorder quantity set</span>
										{/if}
									</td>
								</tr>
							{/each}
						{/if}
					</tbody>
				</table>

				{#if reorderTotalCount > 0}
					<div class="pager">
						<span class="pager-status">
							Page {reorderPage} of {reorderTotalPages}
						</span>
						<div class="pager-buttons">
							<button
								class="pager-btn"
								onclick={() => goToReorderPage(reorderPage - 1)}
								disabled={reorderPage <= 1 || reorderLoading}>Previous</button
							>
							<button
								class="pager-btn"
								onclick={() => goToReorderPage(reorderPage + 1)}
								disabled={reorderPage >= reorderTotalPages || reorderLoading}>Next</button
							>
						</div>
					</div>
				{/if}
			</div>
		{/if}

		<!-- SUGGESTED PURCHASE ORDER -->
		{#if activeTab === 'purchase-order'}
			<div class="panel document">
				<div class="doc-header">
					<div>
						<h3>Suggested Purchase Order</h3>
						<p class="doc-meta">
							Generated {poGeneratedAt || '—'}{getUsername() ? ` by ${getUsername()}` : ''}
						</p>
					</div>
					<button class="btn-outline" onclick={loadPurchaseOrder} disabled={poLoading}>
						{poLoading ? 'Rebuilding…' : 'Rebuild'}
					</button>
				</div>

				<p class="summary-line">
					Every item at or below its reorder point, with how much to buy: whole lots where a reorder
					quantity is recorded, otherwise enough to top the item back up to its point. It names no
					supplier and carries no prices, because the catalogue records neither - this is what to
					buy, for a buyer to price and place.
				</p>

				{#if poUnquantified > 0}
					<p class="summary-line warn">
						{poUnquantified} low item{poUnquantified === 1 ? ' is' : 's are'} left off: sitting exactly
						on the reorder point with no reorder quantity recorded, there is nothing in the catalogue
						to say how much to buy. Set a reorder quantity on the Inventory page to include
						{poUnquantified === 1 ? 'it' : 'them'}.
					</p>
				{/if}

				{#if poTruncated}
					<p class="summary-line warn">
						{poTotalCount} items are below their reorder point and this order lists the {PO_MAX_LINES}
						most urgent - the most one request will carry. Work through these, then rebuild.
					</p>
				{/if}

				<table class="data-table">
					<thead>
						<tr>
							<th>SKU</th>
							<th>ITEM NAME</th>
							<th>CATEGORY</th>
							<th>ON HAND</th>
							<th>REORDER POINT</th>
							<th>ORDER QUANTITY</th>
						</tr>
					</thead>
					<tbody>
						{#if poLoading}
							<tr><td colspan="6" class="empty-state">Building the purchase order...</td></tr>
						{:else if poError}
							<tr><td colspan="6" class="empty-state error">{poError}</td></tr>
						{:else if poRows.length === 0}
							<tr>
								<td colspan="6" class="empty-state">
									Nothing to order - every item with a reorder point is above it.
								</td>
							</tr>
						{:else}
							{#each poRows as row (row.itemId)}
								<tr>
									<td>{row.sku}</td>
									<td><strong>{row.name}</strong></td>
									<td>{row.category}</td>
									<td class:negative={row.quantityOnHand === 0}>
										{row.quantityOnHand.toLocaleString()}
									</td>
									<td>{row.reorderPoint.toLocaleString()}</td>
									<td><strong>{row.suggestedOrderQuantity.toLocaleString()}</strong></td>
								</tr>
							{/each}
						{/if}
					</tbody>
					{#if poRows.length > 0}
						<tfoot>
							<tr>
								<td colspan="5" class="text-right"><strong>Total</strong></td>
								<td>
									<strong>{poUnitsToOrder.toLocaleString()} units</strong>
									<span class="sub"
										>across {poRows.length} line{poRows.length === 1 ? '' : 's'}</span
									>
								</td>
							</tr>
						</tfoot>
					{/if}
				</table>
			</div>
		{/if}

		<!-- DEAD STOCK -->
		{#if activeTab === 'dead-stock'}
			<div class="panel">
				<form class="filters" onsubmit={applyDeadStockFilters}>
					<div class="input-wrap">
						<label for="dead-stock-days">NO MOVEMENT IN (DAYS)</label>
						<input id="dead-stock-days" type="number" min="1" bind:value={deadStockDays} />
					</div>
					<button type="submit" class="btn-solid filter-btn">Apply</button>
				</form>

				<p class="summary-line">
					Stock still on the shelf with no receive, pick, or relocation in the last {deadStockDays}
					days.
				</p>

				<table class="data-table">
					<thead>
						<tr>
							<th>ITEM NAME</th>
							<th>SKU</th>
							<th>CATEGORY</th>
							<th>QUANTITY ON HAND</th>
							<th>LAST MOVEMENT</th>
						</tr>
					</thead>
					<tbody>
						{#if deadStockLoading}
							<tr><td colspan="5" class="empty-state">Loading dead stock...</td></tr>
						{:else if deadStockError}
							<tr><td colspan="5" class="empty-state error">{deadStockError}</td></tr>
						{:else if deadStockRows.length === 0}
							<tr
								><td colspan="5" class="empty-state"
									>Nothing is sitting idle - every SKU on hand has moved recently.</td
								></tr
							>
						{:else}
							{#each deadStockRows as row (row.id)}
								<tr>
									<td><strong>{row.name}</strong></td>
									<td>{row.sku}</td>
									<td>{row.category}</td>
									<td>{row.quantityOnHand.toLocaleString()}</td>
									<td>{formatDate(row.lastMovementAt)}</td>
								</tr>
							{/each}
						{/if}
					</tbody>
				</table>

				{#if deadStockTotalCount > 0}
					<div class="pager">
						<span class="pager-status">
							Page {deadStockPage} of {deadStockTotalPages}
						</span>
						<div class="pager-buttons">
							<button
								class="pager-btn"
								onclick={() => goToDeadStockPage(deadStockPage - 1)}
								disabled={deadStockPage <= 1 || deadStockLoading}>Previous</button
							>
							<button
								class="pager-btn"
								onclick={() => goToDeadStockPage(deadStockPage + 1)}
								disabled={deadStockPage >= deadStockTotalPages || deadStockLoading}>Next</button
							>
						</div>
					</div>
				{/if}
			</div>
		{/if}

		<!-- VELOCITY -->
		{#if activeTab === 'velocity'}
			<div class="panel">
				<form class="filters" onsubmit={applyVelocityFilters}>
					<div class="input-wrap">
						<label for="velocity-days">WINDOW (DAYS)</label>
						<input id="velocity-days" type="number" min="1" bind:value={velocityDays} />
					</div>
					<button type="submit" class="btn-solid filter-btn">Apply</button>
				</form>

				<p class="summary-line">
					Units received and picked per item over the last {velocityDays} days - fastest movers first,
					so the slowest are what to stop reordering.
				</p>

				<table class="data-table">
					<thead>
						<tr>
							<th>ITEM NAME</th>
							<th>SKU</th>
							<th>CATEGORY</th>
							<th>UNITS IN</th>
							<th>UNITS OUT</th>
							<th>NET CHANGE</th>
						</tr>
					</thead>
					<tbody>
						{#if velocityLoading}
							<tr><td colspan="6" class="empty-state">Loading velocity...</td></tr>
						{:else if velocityError}
							<tr><td colspan="6" class="empty-state error">{velocityError}</td></tr>
						{:else if velocityRows.length === 0}
							<tr><td colspan="6" class="empty-state">No items defined yet.</td></tr>
						{:else}
							{#each velocityRows as row (row.id)}
								<tr>
									<td><strong>{row.name}</strong></td>
									<td>{row.sku}</td>
									<td>{row.category}</td>
									<td>{row.unitsIn.toLocaleString()}</td>
									<td>{row.unitsOut.toLocaleString()}</td>
									<td class:positive={row.netChange > 0} class:negative={row.netChange < 0}>
										{row.netChange > 0 ? '+' : ''}{row.netChange.toLocaleString()}
									</td>
								</tr>
							{/each}
						{/if}
					</tbody>
				</table>

				{#if velocityTotalCount > 0}
					<div class="pager">
						<span class="pager-status">
							Page {velocityPage} of {velocityTotalPages}
						</span>
						<div class="pager-buttons">
							<button
								class="pager-btn"
								onclick={() => goToVelocityPage(velocityPage - 1)}
								disabled={velocityPage <= 1 || velocityLoading}>Previous</button
							>
							<button
								class="pager-btn"
								onclick={() => goToVelocityPage(velocityPage + 1)}
								disabled={velocityPage >= velocityTotalPages || velocityLoading}>Next</button
							>
						</div>
					</div>
				{/if}
			</div>
		{/if}
	</main>
{/if}

<style>
	.dashboard-content {
		margin-left: 250px;
		padding: 2rem;
		background: #f8fafc;
		min-height: calc(100vh - 70px);
	}
	.page-header {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		margin-bottom: 1.5rem;
	}
	.page-header h2 {
		margin: 0 0 0.25rem 0;
		color: #0f172a;
	}
	.page-header p {
		margin: 0;
		color: #64748b;
	}
	.actions {
		display: flex;
		gap: 0.75rem;
	}

	.btn-outline {
		background: white;
		border: 1px solid #cbd5e1;
		padding: 0.6rem 1.1rem;
		border-radius: 6px;
		font-weight: 500;
		cursor: pointer;
		color: #334155;
	}
	.btn-outline:hover:not(:disabled) {
		background: #f1f5f9;
	}
	.btn-solid {
		background: #0b6b36;
		color: white;
		border: none;
		padding: 0.6rem 1.1rem;
		border-radius: 6px;
		font-weight: 500;
		cursor: pointer;
	}
	.btn-solid:hover {
		background: #095028;
	}

	.tabs {
		display: flex;
		gap: 0.25rem;
		border-bottom: 1px solid #e2e8f0;
		margin-bottom: 1.5rem;
	}
	.tab {
		background: none;
		border: none;
		border-bottom: 2px solid transparent;
		padding: 0.75rem 1rem;
		font-size: 0.9rem;
		font-weight: 600;
		color: #64748b;
		cursor: pointer;
	}
	.tab:hover {
		color: #0f172a;
	}
	.tab.active {
		color: #0b6b36;
		border-bottom-color: #0b6b36;
	}

	.panel {
		background: white;
		padding: 1.5rem;
		border-radius: 8px;
		border: 1px solid #e2e8f0;
	}

	.filters {
		display: flex;
		flex-wrap: wrap;
		align-items: flex-end;
		gap: 1rem;
		margin-bottom: 1rem;
	}
	.select-wrap {
		min-width: 180px;
	}
	.select-wrap :global(.input-group),
	.input-wrap {
		margin-bottom: 0;
	}
	.input-wrap {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}
	.input-wrap label {
		font-size: 0.75rem;
		font-weight: 600;
		color: #475569;
		letter-spacing: 0.5px;
	}
	.input-wrap input {
		padding: 0.75rem;
		border: 1px solid #cbd5e1;
		border-radius: 6px;
		font-family: inherit;
		outline: none;
	}
	.input-wrap input:focus {
		border-color: #0b6b36;
	}
	.filter-btn {
		height: 42px;
	}

	.summary-line {
		margin: 0 0 1rem 0;
		font-size: 0.85rem;
		color: #64748b;
	}
	.summary-line.strong {
		color: #0f172a;
		font-weight: 600;
	}
	.summary-line.warn {
		background: #fffbeb;
		border: 1px solid #fcd34d;
		border-radius: 6px;
		padding: 0.75rem;
		color: #92400e;
	}

	/* The purchase order reads as a document rather than as another table of
		figures, because it is the one thing on this page someone acts on and
		hands to somebody else. */
	.doc-header {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		gap: 1rem;
		margin-bottom: 1rem;
		padding-bottom: 1rem;
		border-bottom: 2px solid #0f172a;
	}
	.doc-header h3 {
		margin: 0 0 0.25rem 0;
		font-size: 1.25rem;
		color: #0f172a;
	}
	.doc-meta {
		margin: 0;
		font-size: 0.8rem;
		color: #64748b;
	}
	.data-table tfoot td {
		border-top: 2px solid #0f172a;
		border-bottom: none;
		color: #0f172a;
	}
	.text-right {
		text-align: right;
	}

	.data-table {
		width: 100%;
		border-collapse: collapse;
		text-align: left;
	}
	.data-table th {
		padding: 1rem;
		border-bottom: 2px solid #e2e8f0;
		color: #64748b;
		font-size: 0.75rem;
		letter-spacing: 0.5px;
		white-space: nowrap;
	}
	.data-table td {
		padding: 1rem;
		border-bottom: 1px solid #e2e8f0;
		font-size: 0.9rem;
		color: #475569;
	}
	.data-table td strong {
		color: #0f172a;
	}
	.data-table td .sub {
		display: block;
		font-size: 0.75rem;
		color: #94a3b8;
	}
	.data-table td.positive {
		color: #166534;
		font-weight: 600;
	}
	.data-table td.negative {
		color: #991b1b;
		font-weight: 600;
	}

	.badge {
		padding: 0.25rem 0.6rem;
		border-radius: 20px;
		font-size: 0.7rem;
		font-weight: 700;
		letter-spacing: 0.3px;
	}
	.badge.in {
		background: #dcfce7;
		color: #166534;
	}
	.badge.out {
		background: #fee2e2;
		color: #991b1b;
	}
	.badge.move {
		background: #e0f2fe;
		color: #0369a1;
	}

	.pager {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-top: 1rem;
	}
	.pager-status {
		font-size: 0.8rem;
		color: #64748b;
	}
	.pager-buttons {
		display: flex;
		gap: 0.5rem;
	}
	.pager-btn {
		background: white;
		color: #475569;
		border: 1px solid #cbd5e1;
		padding: 0.5rem 1rem;
		border-radius: 6px;
		font-weight: 500;
		cursor: pointer;
	}
	.pager-btn:hover:not(:disabled) {
		background: #f1f5f9;
	}
	.pager-btn:disabled {
		color: #94a3b8;
		border-color: #e2e8f0;
		cursor: not-allowed;
	}

	.empty-state {
		text-align: center;
		padding: 2rem;
		color: #64748b;
		font-style: italic;
	}
	.empty-state.error {
		color: #ef4444;
		font-style: normal;
	}

	/* Printing is on this page because of the purchase order: what comes out of
		the printer should be the document, not the application around it. The
		sidebar and header are other components, so reaching them needs :global. */
	@media print {
		:global(.sidebar),
		:global(.top-header) {
			display: none;
		}
		.dashboard-content {
			margin-left: 0;
			padding: 0;
			background: white;
		}
		.actions,
		.tabs,
		.filters,
		.pager {
			display: none;
		}
		.panel {
			border: none;
			padding: 0;
		}
	}
</style>
