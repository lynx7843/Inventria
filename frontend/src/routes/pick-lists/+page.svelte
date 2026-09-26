<script lang="ts">
	import Sidebar from '$lib/components/shared/Sidebar.svelte';
	import Header from '$lib/components/shared/Header.svelte';
	import Button from '$lib/components/shared/Button.svelte';
	import SelectField from '$lib/components/shared/SelectField.svelte';
	import { onMount } from 'svelte';
	import { resolve } from '$app/paths';
	import type { ResolvedPathname } from '$app/types';
	import { apiFetch, apiErrorMessage } from '$lib/api';
	import { endExpiredSession, requireSession } from '$lib/auth';
	import { focusId, isTypingInField } from '$lib/keyboard';
	import {
		fetchAllItems,
		fetchBins,
		itemLabel,
		binLabel,
		parseUnits,
		type Option
	} from '$lib/inventory';
	import {
		fetchPickLists,
		fetchPickList,
		isLineOutstanding,
		lineBinLabel,
		PICK_LIST_STATUSES,
		type PickListSummary,
		type PickListDetail,
		type PickListLine
	} from '$lib/pickLists';

	// Gates the markup below. No role list: a pick list is a stock movement in
	// batch form, and picking is already open to Admins and Employees alike.
	let allowed = $state(false);

	let lists: PickListSummary[] = $state([]);
	let isLoading = $state(true);
	let errorMsg = $state('');

	const pageSize = 15;
	let page = $state(1);
	let totalPages = $state(1);
	let totalCount = $state(0);

	// Open first, because an Open list is the only one anyone has anything left to
	// do with - the completed ones are history and would otherwise bury it.
	let statusFilter = $state('Open');

	let itemOptions: Option[] = $state([]);
	let binOptions: Option[] = $state([]);
	let isLoadingPickers = $state(true);

	async function loadLists() {
		isLoading = true;
		try {
			const result = await fetchPickLists({
				status: statusFilter || undefined,
				page,
				pageSize
			});

			lists = result.pickLists;
			totalPages = result.totalPages;
			totalCount = result.totalCount;

			// Cancelling or completing the last list on a page leaves you looking at
			// an empty page that is not the empty state - step back to one with rows.
			if (lists.length === 0 && page > 1) {
				page = Math.min(page - 1, Math.max(result.totalPages, 1));
				await loadLists();
			}
		} catch (err) {
			console.error(err);
			errorMsg = err instanceof Error ? err.message : 'Failed to load pick lists.';
		} finally {
			isLoading = false;
		}
	}

	async function loadPickers() {
		isLoadingPickers = true;
		try {
			const [items, bins] = await Promise.all([fetchAllItems(), fetchBins()]);
			itemOptions = items.map((item) => ({ value: item.id, label: itemLabel(item) }));
			binOptions = bins.map((bin) => ({ value: bin.id, label: binLabel(bin) }));
		} catch (err) {
			console.error(err);
			errorMsg = err instanceof Error ? err.message : 'Failed to load items and bins.';
		} finally {
			isLoadingPickers = false;
		}
	}

	onMount(async () => {
		if (!requireSession()) return;
		allowed = true;

		// The pick sheet's Back link returns here with ?list=<id>, meaning "I was
		// looking at that one" - so reopen it rather than making someone find it
		// in the table again. Read the same way /inventory reads its ?new=1.
		const requested = Number(new URLSearchParams(window.location.search).get('list'));

		loadPickers();
		await loadLists();

		if (Number.isInteger(requested) && requested > 0) viewList(requested);
	});

	function goToPage(next: number) {
		if (next < 1 || next > totalPages || next === page) return;
		page = next;
		loadLists();
	}

	function applyFilters() {
		page = 1;
		loadLists();
	}

	function statusBadgeClass(status: string): string {
		switch (status) {
			case 'Open':
				return 'blue';
			case 'Completed':
				return 'green';
			case 'Cancelled':
				return 'red';
			default:
				return 'gray';
		}
	}

	function formatDate(iso: string | null): string {
		return iso ? new Date(iso).toLocaleDateString() : '—';
	}

	function formatDateTime(iso: string | null): string {
		return iso ? new Date(iso).toLocaleString() : '—';
	}

	/**
	 * Where the printable sheet for a list lives. See /print/pick-sheet - and
	 * bins/+page.svelte's labelSheetHref for why the query string is built inside
	 * resolve() rather than appended to it.
	 */
	function pickSheetHref(id: number): ResolvedPathname {
		return resolve(`/print/pick-sheet?list=${id}`);
	}

	// --- OPEN A LIST --------------------------------------------------------

	type LineDraft = { itemId: string; warehouseBinId: string; quantity: string; lotNumber: string };

	let showForm = $state(false);
	let formLines: LineDraft[] = $state([]);
	let formError = $state('');
	let saving = $state(false);

	function blankLine(): LineDraft {
		return { itemId: '', warehouseBinId: '', quantity: '', lotNumber: '' };
	}

	function openCreateForm() {
		formLines = [blankLine()];
		formError = '';
		viewingList = null;
		showForm = true;
	}

	function closeForm() {
		showForm = false;

		// See bins/+page.svelte: whatever had focus (Cancel, or the
		// just-submitted Save) is gone the moment the panel closes.
		focusId('create-pick-list-trigger');
	}

	function addLine() {
		formLines = [...formLines, blankLine()];
	}

	function removeLine(index: number) {
		if (formLines.length <= 1) return;
		formLines = formLines.filter((_, i) => i !== index);
	}

	async function saveList() {
		formError = '';

		const lines = [];
		for (const line of formLines) {
			// A row nobody filled in is a row nobody meant - skipped rather than
			// refused, the same way the purchase order form treats its blanks.
			if (!line.itemId && !line.warehouseBinId && !line.quantity.trim()) continue;

			if (!line.itemId || !line.warehouseBinId) {
				formError = 'Every line needs both an item and the bin to pick it from.';
				return;
			}

			const quantity = parseUnits(line.quantity);
			if (quantity === null) {
				formError = 'Every line needs a quantity that is a whole number greater than zero.';
				return;
			}

			lines.push({
				itemId: Number(line.itemId),
				warehouseBinId: Number(line.warehouseBinId),
				quantity,
				lotNumber: line.lotNumber.trim() || null
			});
		}

		if (lines.length === 0) {
			formError = 'Add at least one line with an item, a bin and a quantity.';
			return;
		}

		saving = true;

		try {
			const res = await apiFetch('/api/pick-lists', {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ lines })
			});

			if (res.status === 401) {
				endExpiredSession();
				return;
			}

			if (!res.ok) {
				// A missing item or bin, or an item that tracks lots with no lot
				// named. The server says which.
				formError = await apiErrorMessage(res, 'Failed to open pick list.');
				return;
			}

			// The API hands back the list it just built, already sorted into its
			// walking route - showing it is the point, since the route is the thing
			// the person asked for and it is not the order they typed.
			const created = (await res.json()).pickList as PickListDetail;

			closeForm();
			page = 1;
			statusFilter = 'Open';
			await loadLists();
			viewingList = created;
		} catch (err) {
			console.error(err);
			formError = 'A network error occurred while opening the pick list.';
		} finally {
			saving = false;
		}
	}

	// --- VIEW / PICK --------------------------------------------------------

	let viewingList: PickListDetail | null = $state(null);
	let viewLoading = $state(false);
	let viewError = $state('');
	let actionError = $state('');
	let actionLoading = $state(false);

	async function viewList(id: number) {
		showForm = false;
		viewError = '';
		actionError = '';
		viewLoading = true;
		try {
			viewingList = await fetchPickList(id);
		} catch (err) {
			console.error(err);
			viewingList = null;
			viewError = err instanceof Error ? err.message : 'Failed to load the pick list.';
		} finally {
			viewLoading = false;
		}
	}

	function closeView() {
		viewingList = null;
		viewError = '';
		actionError = '';
	}

	async function cancelList() {
		if (!viewingList) return;
		if (!confirm('Cancel this pick list? Anything already picked stays picked.')) return;

		actionError = '';
		actionLoading = true;

		try {
			const res = await apiFetch(`/api/pick-lists/${viewingList.id}/cancel`, { method: 'POST' });

			if (res.status === 401) {
				endExpiredSession();
				return;
			}

			if (!res.ok) {
				// Already Completed or Cancelled - someone else got there first.
				actionError = await apiErrorMessage(res, 'Failed to cancel this pick list.');
				return;
			}

			await viewList(viewingList.id);
			await loadLists();
		} catch (err) {
			console.error(err);
			actionError = 'A network error occurred while cancelling.';
		} finally {
			actionLoading = false;
		}
	}

	// One line's pick form at a time - a picker is standing at one bin, and this
	// keeps the state below to two fields rather than one pair per line.
	let pickingLineId: number | null = $state(null);
	let pickQuantity = $state('');
	let pickError = $state('');
	let picking = $state(false);

	function openPickForm(line: PickListLine) {
		pickingLineId = line.id;
		// Prefilled with what the line still owes, because finding the full amount
		// is the ordinary outcome; it is edited only to record a short pick.
		pickQuantity = String(line.quantityRemaining);
		pickError = '';
	}

	function closePickForm() {
		pickingLineId = null;
	}

	async function submitPick(line: PickListLine) {
		if (!viewingList) return;
		pickError = '';

		const units = parseUnits(pickQuantity);
		if (units === null) {
			pickError = 'Enter the number of units as a whole number greater than zero.';
			return;
		}

		picking = true;

		try {
			const res = await apiFetch(`/api/pick-lists/${viewingList.id}/lines/${line.id}/pick`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ quantity: units })
			});

			if (res.status === 401) {
				endExpiredSession();
				return;
			}

			if (!res.ok) {
				// Not enough on the shelf, more than the line owes, or the list
				// closed out from under this tab. The server says which.
				pickError = await apiErrorMessage(res, 'Failed to pick this line.');
				return;
			}

			// The response carries the whole list back, including the status flip to
			// Completed once the last line is done - so take it rather than refetch.
			viewingList = (await res.json()).pickList as PickListDetail;
			pickingLineId = null;
			await loadLists();
		} catch (err) {
			console.error(err);
			pickError = 'A network error occurred while picking.';
		} finally {
			picking = false;
		}
	}

	// See bins/+page.svelte for both shortcuts and why Escape alone ignores
	// isTypingInField.
	function handleGlobalKeydown(event: KeyboardEvent) {
		if (event.key === 'Escape' && showForm) {
			event.preventDefault();
			closeForm();
			return;
		}

		if (isTypingInField(event.target) || event.altKey || event.ctrlKey || event.metaKey) return;

		if (event.key === 'n' && !showForm) {
			event.preventDefault();
			openCreateForm();
		}
	}
</script>

<svelte:window onkeydown={handleGlobalKeydown} />

{#if allowed}
	<Sidebar activePage="Pick Lists" />
	<Header />

	<main class="dashboard-content">
		<div class="page-header">
			<div>
				<h2>Pick Lists</h2>
				<p>Batch the picks for one trip through the warehouse, then print the sheet and walk it.</p>
			</div>
			{#if !showForm}
				<button id="create-pick-list-trigger" class="btn-solid" onclick={openCreateForm}>
					+ New Pick List <kbd>N</kbd>
				</button>
			{/if}
		</div>

		{#if errorMsg}
			<div class="alert alert-error">{errorMsg}</div>
		{/if}

		{#if showForm}
			<div class="panel form-panel">
				<h3>Open a Pick List</h3>
				<p class="hint">
					Add every pick that belongs to this trip. The route is worked out on save, by bin address
					- the order typed here does not matter.
				</p>
				<form
					onsubmit={(e) => {
						e.preventDefault();
						saveList();
					}}
				>
					<div class="lines-editor">
						<div class="lines-header">
							<span>ITEM</span>
							<span>BIN</span>
							<span>QTY</span>
							<span>LOT/BATCH</span>
							<span></span>
						</div>
						{#each formLines as line, index (index)}
							<div class="line-row">
								<select bind:value={line.itemId} disabled={isLoadingPickers}>
									<option value="" disabled>
										{isLoadingPickers ? 'Loading...' : 'Select an item'}
									</option>
									{#each itemOptions as option (option.value)}
										<option value={String(option.value)}>{option.label}</option>
									{/each}
								</select>
								<select bind:value={line.warehouseBinId} disabled={isLoadingPickers}>
									<option value="" disabled>
										{isLoadingPickers ? 'Loading...' : 'Select a bin'}
									</option>
									{#each binOptions as option (option.value)}
										<option value={String(option.value)}>{option.label}</option>
									{/each}
								</select>
								<input type="number" min="1" step="1" placeholder="0" bind:value={line.quantity} />
								<input type="text" placeholder="If tracked" bind:value={line.lotNumber} />
								<button
									type="button"
									class="btn-icon"
									onclick={() => removeLine(index)}
									disabled={formLines.length <= 1}
									aria-label="Remove line {index + 1}">🗑️</button
								>
							</div>
						{/each}
						<button type="button" class="link-btn" onclick={addLine}>+ Add another line</button>
					</div>

					{#if formError}
						<span class="field-error">{formError}</span>
					{/if}

					<div class="actions">
						<button type="button" class="btn-outline" onclick={closeForm} disabled={saving}>
							Cancel
						</button>
						<div class="submit-wrap">
							<Button
								type="submit"
								text="Open Pick List"
								isLoading={saving}
								loadingText="OPENING..."
							/>
						</div>
					</div>
				</form>
			</div>
		{/if}

		{#if viewingList || viewLoading || viewError}
			<div class="panel view-panel">
				{#if viewLoading}
					<p class="empty-state">Loading pick list…</p>
				{:else if viewingList}
					<div class="view-header">
						<div>
							<h3>
								Pick List #{viewingList.id}
								<span class="badge {statusBadgeClass(viewingList.status)}"
									>{viewingList.status}</span
								>
							</h3>
							<p class="subtitle">
								{viewingList.lines.length} line{viewingList.lines.length === 1 ? '' : 's'}, in
								walking order.
							</p>
						</div>
						<button type="button" class="btn-outline" onclick={closeView}>Close</button>
					</div>

					<div class="view-meta">
						<div><span class="meta-label">Opened</span>{formatDateTime(viewingList.createdAt)}</div>
						<div><span class="meta-label">By</span>{viewingList.createdBy || '—'}</div>
						<div>
							<span class="meta-label">Completed</span>{formatDateTime(viewingList.completedAt)}
						</div>
					</div>

					{#if actionError}
						<div class="alert alert-error">{actionError}</div>
					{/if}

					<table class="data-table">
						<thead>
							<tr>
								<th>#</th>
								<th>BIN</th>
								<th>ITEM</th>
								<th>REQUESTED</th>
								<th>PICKED</th>
								<th>REMAINING</th>
								<th class="text-right">ACTIONS</th>
							</tr>
						</thead>
						<tbody>
							{#each viewingList.lines as line (line.id)}
								<tr>
									<td class="text-muted">{line.sequence}</td>
									<td><span class="badge gray mono">{lineBinLabel(line)}</span></td>
									<td>
										<strong>{line.itemName ?? `Item #${line.itemId}`}</strong>
										{#if line.itemSku}<span class="text-muted"> · {line.itemSku}</span>{/if}
										{#if line.lotNumber}<span class="text-muted"> · Lot {line.lotNumber}</span>{/if}
									</td>
									<td>{line.quantityRequested.toLocaleString()}</td>
									<td>{line.quantityPicked.toLocaleString()}</td>
									<td>
										{line.quantityRemaining.toLocaleString()}
										{#if isLineOutstanding(line) && line.quantityPicked > 0}
											<span class="badge amber">SHORT</span>
										{/if}
									</td>
									<td class="text-right">
										{#if viewingList.status === 'Open' && isLineOutstanding(line)}
											<button class="btn-outline small" onclick={() => openPickForm(line)}>
												Pick
											</button>
										{/if}
									</td>
								</tr>
								{#if pickingLineId === line.id}
									<tr class="pick-row">
										<td colspan="7">
											<form
												class="pick-form"
												onsubmit={(e) => {
													e.preventDefault();
													submitPick(line);
												}}
											>
												<label for="pick-qty-{line.id}">
													Units picked from {lineBinLabel(line)}
												</label>
												<input
													id="pick-qty-{line.id}"
													type="number"
													min="1"
													step="1"
													max={line.quantityRemaining}
													bind:value={pickQuantity}
												/>
												<button type="submit" class="btn-solid small" disabled={picking}>
													{picking ? 'Picking…' : 'Confirm'}
												</button>
												<button
													type="button"
													class="link-btn"
													onclick={closePickForm}
													disabled={picking}>Cancel</button
												>
											</form>
											{#if pickError}
												<span class="field-error">{pickError}</span>
											{/if}
										</td>
									</tr>
								{/if}
							{/each}
						</tbody>
					</table>

					<div class="view-actions">
						{#if viewingList.status === 'Open'}
							<button
								type="button"
								class="btn-outline"
								onclick={cancelList}
								disabled={actionLoading}>Cancel List</button
							>
						{/if}
						<!-- A link rather than a button: the sheet is a page of its own, so
						     it can be opened in another tab and left up while the picks are
						     keyed back in. -->
						<a class="btn-solid" href={pickSheetHref(viewingList.id)}>Print Pick Sheet</a>
					</div>
				{:else if viewError}
					<div class="alert alert-error">{viewError}</div>
				{/if}
			</div>
		{/if}

		<div class="panel">
			<div class="filters">
				<SelectField
					id="status-filter"
					label="STATUS"
					options={PICK_LIST_STATUSES.map((s) => ({ value: s, label: s }))}
					bind:value={statusFilter}
					placeholder="All statuses"
					required={false}
				/>
				<div class="filter-actions">
					<button class="btn-outline" onclick={applyFilters}>Apply</button>
					<button
						class="btn-outline"
						onclick={() => {
							statusFilter = '';
							applyFilters();
						}}>Clear</button
					>
				</div>
			</div>

			<table class="data-table">
				<thead>
					<tr>
						<th>ID</th>
						<th>STATUS</th>
						<th>OPENED</th>
						<th>BY</th>
						<th>LINES</th>
						<th>REMAINING</th>
						<th class="text-right">ACTIONS</th>
					</tr>
				</thead>
				<tbody>
					{#if isLoading}
						<tr><td colspan="7" class="empty-state">Loading pick lists...</td></tr>
					{:else if lists.length === 0}
						<tr
							><td colspan="7" class="empty-state">
								No pick lists found. Open one above to batch a trip through the warehouse.
							</td></tr
						>
					{:else}
						{#each lists as list (list.id)}
							<tr>
								<td class="text-muted">#{list.id}</td>
								<td><span class="badge {statusBadgeClass(list.status)}">{list.status}</span></td>
								<td class="text-muted">{formatDate(list.createdAt)}</td>
								<td>{list.createdBy || '—'}</td>
								<td>{list.lineCount.toLocaleString()}</td>
								<td>{list.linesRemaining.toLocaleString()}</td>
								<td class="text-right action-btns">
									<button class="btn-outline small" onclick={() => viewList(list.id)}>View</button>
									<a
										class="btn-outline small"
										href={pickSheetHref(list.id)}
										aria-label="Print pick sheet for list {list.id}">🖨️ Sheet</a
									>
								</td>
							</tr>
						{/each}
					{/if}
				</tbody>
			</table>

			{#if totalCount > 0}
				<div class="pager">
					<span class="pager-status">
						Showing {lists.length} of {totalCount} list{totalCount === 1 ? '' : 's'} &middot; page {page}
						of {totalPages}
					</span>
					<div class="pager-buttons">
						<button
							class="btn-outline"
							onclick={() => goToPage(page - 1)}
							disabled={page <= 1 || isLoading}>Previous</button
						>
						<button
							class="btn-outline"
							onclick={() => goToPage(page + 1)}
							disabled={page >= totalPages || isLoading}>Next</button
						>
					</div>
				</div>
			{/if}
		</div>
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
		align-items: center;
		margin-bottom: 2rem;
	}
	.page-header h2 {
		margin: 0 0 0.25rem 0;
		color: #0f172a;
	}
	.page-header p {
		margin: 0;
		color: #64748b;
	}

	.btn-solid {
		background: #0b6b36;
		color: white;
		border: none;
		padding: 0.6rem 1.2rem;
		border-radius: 6px;
		font-weight: 600;
		cursor: pointer;
		text-decoration: none;
		display: inline-block;
	}
	.btn-solid:hover {
		background: #095028;
	}
	.btn-solid kbd {
		margin-left: 0.35rem;
		display: inline-block;
		min-width: 1.1rem;
		padding: 0.05rem 0.35rem;
		border: 1px solid rgb(255 255 255 / 40%);
		border-bottom-width: 2px;
		border-radius: 4px;
		background: rgb(255 255 255 / 15%);
		font-family: inherit;
		font-size: 0.7rem;
		font-weight: 700;
		text-align: center;
	}
	.btn-solid:disabled {
		background: #94a3b8;
		cursor: not-allowed;
	}
	.btn-outline {
		background: white;
		color: #475569;
		border: 1px solid #cbd5e1;
		padding: 0.6rem 1.2rem;
		border-radius: 6px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
		text-decoration: none;
		display: inline-block;
	}
	.btn-outline:hover {
		background: #f1f5f9;
	}
	.btn-outline:disabled {
		color: #94a3b8;
		border-color: #e2e8f0;
		cursor: not-allowed;
		background: white;
	}
	.btn-outline.small,
	.btn-solid.small {
		padding: 0.4rem 0.8rem;
		font-size: 0.8rem;
	}
	.link-btn {
		background: none;
		border: none;
		padding: 0;
		color: #0b6b36;
		font-weight: 600;
		font-size: 0.8rem;
		cursor: pointer;
		font-family: inherit;
	}
	.link-btn:hover {
		text-decoration: underline;
	}
	.link-btn:disabled {
		color: #94a3b8;
		cursor: not-allowed;
	}

	.panel {
		background: white;
		padding: 1.5rem;
		border-radius: 8px;
		border: 1px solid #e2e8f0;
		margin-bottom: 2rem;
	}
	.form-panel h3,
	.view-header h3 {
		margin: 0 0 1.5rem 0;
		font-size: 1.1rem;
		color: #0f172a;
		border-bottom: 1px solid #e2e8f0;
		padding-bottom: 0.75rem;
	}
	.hint {
		margin: -0.75rem 0 1.25rem 0;
		font-size: 0.85rem;
		color: #64748b;
	}
	.field-error {
		display: block;
		font-size: 0.75rem;
		color: #b91c1c;
		margin-top: 0.25rem;
	}

	.lines-editor {
		margin-bottom: 1.5rem;
	}
	.lines-header,
	.line-row {
		display: grid;
		grid-template-columns: 2fr 1.2fr 0.7fr 1fr auto;
		gap: 0.75rem;
		align-items: center;
	}
	.lines-header {
		font-size: 0.7rem;
		font-weight: 600;
		letter-spacing: 0.5px;
		color: #64748b;
		margin-bottom: 0.5rem;
	}
	.line-row {
		margin-bottom: 0.5rem;
	}
	.line-row select,
	.line-row input {
		padding: 0.6rem 0.75rem;
		border: 1px solid #cbd5e1;
		border-radius: 6px;
		font-family: inherit;
		font-size: 0.85rem;
		width: 100%;
		box-sizing: border-box;
	}

	.actions {
		display: flex;
		justify-content: flex-end;
		gap: 1rem;
		align-items: center;
	}
	.submit-wrap {
		width: 170px;
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
	.empty-state {
		text-align: center;
		padding: 3rem;
		color: #64748b;
		font-style: italic;
	}

	.text-right {
		text-align: right;
	}
	.text-muted {
		color: #94a3b8;
		font-size: 0.8rem;
	}
	.action-btns {
		display: flex;
		gap: 0.5rem;
		justify-content: flex-end;
	}

	.badge {
		padding: 0.25rem 0.5rem;
		border-radius: 4px;
		font-size: 0.7rem;
		font-weight: 700;
		letter-spacing: 0.3px;
		margin-left: 0.5rem;
	}
	.badge.mono {
		font-family: monospace;
		margin-left: 0;
	}
	.badge.gray {
		background: #f1f5f9;
		color: #475569;
		border: 1px solid #e2e8f0;
	}
	.badge.blue {
		background: #dbeafe;
		color: #1e40af;
		border: 1px solid #93c5fd;
	}
	.badge.amber {
		background: #fef3c7;
		color: #92400e;
		border: 1px solid #fcd34d;
	}
	.badge.green {
		background: #dcfce7;
		color: #166534;
		border: 1px solid #4ade80;
	}
	.badge.red {
		background: #fee2e2;
		color: #991b1b;
		border: 1px solid #f87171;
	}

	.view-header {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		border-bottom: none;
	}
	.view-header .subtitle {
		margin: 0;
		color: #64748b;
		font-size: 0.9rem;
	}
	.view-meta {
		display: grid;
		grid-template-columns: repeat(3, 1fr);
		gap: 1rem;
		margin: 1rem 0 1.5rem;
		font-size: 0.85rem;
		color: #475569;
	}
	.meta-label {
		display: block;
		font-size: 0.7rem;
		font-weight: 600;
		letter-spacing: 0.5px;
		color: #94a3b8;
		margin-bottom: 0.15rem;
	}
	.view-actions {
		display: flex;
		justify-content: flex-end;
		gap: 0.75rem;
		margin-top: 1.5rem;
		align-items: center;
	}

	.pick-row td {
		background: #f8fafc;
		padding: 1rem;
	}
	.pick-form {
		display: flex;
		gap: 0.75rem;
		align-items: center;
	}
	.pick-form label {
		font-size: 0.8rem;
		font-weight: 600;
		color: #475569;
	}
	.pick-form input {
		padding: 0.5rem 0.75rem;
		border: 1px solid #cbd5e1;
		border-radius: 6px;
		font-family: inherit;
		font-size: 0.85rem;
		width: 120px;
	}

	.btn-icon {
		background: none;
		border: none;
		font-size: 1.1rem;
		cursor: pointer;
		opacity: 0.6;
		transition: opacity 0.2s;
		padding: 0.25rem;
	}
	.btn-icon:hover {
		opacity: 1;
	}
	.btn-icon:disabled {
		opacity: 0.3;
		cursor: not-allowed;
	}

	.filters {
		display: grid;
		grid-template-columns: 1fr auto;
		gap: 1rem;
		align-items: end;
		margin-bottom: 1rem;
	}
	.filter-actions {
		display: flex;
		gap: 0.5rem;
		margin-bottom: 1.5rem;
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

	.alert {
		padding: 0.75rem;
		border-radius: 6px;
		font-size: 0.85rem;
		margin-bottom: 1.5rem;
		font-weight: 500;
	}
	.alert-error {
		background: #fee2e2;
		color: #991b1b;
		border: 1px solid #f87171;
	}
</style>
