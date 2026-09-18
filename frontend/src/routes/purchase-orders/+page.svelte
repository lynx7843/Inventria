<script lang="ts">
	import Sidebar from '$lib/components/shared/Sidebar.svelte';
	import Header from '$lib/components/shared/Header.svelte';
	import InputField from '$lib/components/shared/InputField.svelte';
	import SelectField from '$lib/components/shared/SelectField.svelte';
	import Button from '$lib/components/shared/Button.svelte';
	import { onMount } from 'svelte';
	import { apiFetch, apiErrorMessage } from '$lib/api';
	import { endExpiredSession, requireSession } from '$lib/auth';
	import {
		fetchAllItems,
		fetchBins,
		itemLabel,
		binLabel,
		parseUnits,
		type Option
	} from '$lib/inventory';
	import { fetchSuppliers, type Supplier } from '$lib/suppliers';
	import {
		fetchPurchaseOrders,
		fetchPurchaseOrder,
		isLineOutstanding,
		PURCHASE_ORDER_STATUSES,
		type PurchaseOrderSummary,
		type PurchaseOrderDetail,
		type PurchaseOrderLine
	} from '$lib/purchaseOrders';

	// Gates the markup below. No role list: purchase orders are part of the
	// same inventory workflow Inventory and Bins already open to anyone
	// signed in.
	let allowed = $state(false);

	let orders: PurchaseOrderSummary[] = $state([]);
	let isLoading = $state(true);
	let errorMsg = $state('');

	const pageSize = 15;
	let page = $state(1);
	let totalPages = $state(1);
	let totalCount = $state(0);

	let statusFilter = $state('');
	let supplierFilter = $state('');

	let suppliers: Supplier[] = $state([]);
	let supplierOptions: Option[] = $state([]);
	let itemOptions: Option[] = $state([]);
	let binOptions: Option[] = $state([]);
	let isLoadingPickers = $state(true);

	async function loadOrders() {
		isLoading = true;
		try {
			const result = await fetchPurchaseOrders({
				status: statusFilter || undefined,
				supplierId: supplierFilter ? Number(supplierFilter) : undefined,
				page,
				pageSize
			});

			orders = result.orders;
			totalPages = result.totalPages;
			totalCount = result.totalCount;

			if (orders.length === 0 && page > 1) {
				page = Math.min(page - 1, Math.max(result.totalPages, 1));
				await loadOrders();
			}
		} catch (err) {
			console.error(err);
			errorMsg = err instanceof Error ? err.message : 'Failed to load purchase orders.';
		} finally {
			isLoading = false;
		}
	}

	async function loadPickers() {
		isLoadingPickers = true;
		try {
			const [supplierList, items, bins] = await Promise.all([
				fetchSuppliers(),
				fetchAllItems(),
				fetchBins()
			]);
			suppliers = supplierList;
			supplierOptions = supplierList.map((s) => ({ value: s.id, label: s.name }));
			itemOptions = items.map((item) => ({ value: item.id, label: itemLabel(item) }));
			binOptions = bins.map((bin) => ({ value: bin.id, label: binLabel(bin) }));
		} catch (err) {
			console.error(err);
			errorMsg = err instanceof Error ? err.message : 'Failed to load suppliers, items, and bins.';
		} finally {
			isLoadingPickers = false;
		}
	}

	function goToPage(next: number) {
		if (next < 1 || next > totalPages || next === page) return;
		page = next;
		errorMsg = '';
		loadOrders();
	}

	function applyFilters() {
		page = 1;
		loadOrders();
	}

	onMount(() => {
		if (!requireSession()) return;
		allowed = true;
		loadPickers();
		loadOrders();
	});

	// --- STATUS DISPLAY --------------------------------------------------------

	function statusBadgeClass(status: string): string {
		switch (status) {
			case 'Draft':
				return 'gray';
			case 'Ordered':
				return 'blue';
			case 'PartiallyReceived':
				return 'amber';
			case 'Received':
				return 'green';
			case 'Cancelled':
				return 'red';
			default:
				return 'gray';
		}
	}

	function statusLabel(status: string): string {
		return status === 'PartiallyReceived' ? 'Partially Received' : status;
	}

	function formatDate(iso: string | null): string {
		return iso ? new Date(iso).toLocaleDateString() : '—';
	}

	function formatDateTime(iso: string | null): string {
		return iso ? new Date(iso).toLocaleString() : '—';
	}

	// --- CREATE / EDIT (DRAFT ONLY) ---------------------------------------------

	type LineDraft = { itemId: string; quantityOrdered: string; unitCost: string };

	let showForm = $state(false);
	let isEditing = $state(false);
	let editingId: number | null = $state(null);
	let formSupplierId = $state('');
	let formExpectedDate = $state('');
	let formNotes = $state('');
	let formLines: LineDraft[] = $state([]);
	let formError = $state('');
	let saving = $state(false);

	// The "add a supplier without leaving the page" mini-form - there is no
	// dedicated Suppliers screen yet, and making someone abandon a purchase
	// order in progress to go create one first would be a worse experience
	// than a two-field form next to the picker that needs it.
	let showNewSupplier = $state(false);
	let newSupplierName = $state('');
	let newSupplierError = $state('');
	let creatingSupplier = $state(false);

	function blankLine(): LineDraft {
		return { itemId: '', quantityOrdered: '', unitCost: '' };
	}

	function openCreateForm() {
		isEditing = false;
		editingId = null;
		formSupplierId = '';
		formExpectedDate = '';
		formNotes = '';
		formLines = [blankLine()];
		formError = '';
		showNewSupplier = false;
		viewingOrder = null;
		showForm = true;
	}

	function openEditForm(order: PurchaseOrderDetail) {
		isEditing = true;
		editingId = order.id;
		formSupplierId = String(order.supplierId);
		formExpectedDate = order.expectedDate ? order.expectedDate.slice(0, 10) : '';
		formNotes = order.notes ?? '';
		formLines = order.lines.map((line) => ({
			itemId: String(line.itemId),
			quantityOrdered: String(line.quantityOrdered),
			unitCost: line.unitCost !== null ? String(line.unitCost) : ''
		}));
		formError = '';
		showNewSupplier = false;
		viewingOrder = null;
		showForm = true;
	}

	function closeForm() {
		showForm = false;
	}

	function addLine() {
		formLines = [...formLines, blankLine()];
	}

	function removeLine(index: number) {
		if (formLines.length <= 1) return;
		formLines = formLines.filter((_, i) => i !== index);
	}

	async function createSupplierInline() {
		newSupplierError = '';
		if (!newSupplierName.trim()) {
			newSupplierError = 'Enter a supplier name.';
			return;
		}

		creatingSupplier = true;
		try {
			const res = await apiFetch('/api/suppliers', {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ name: newSupplierName.trim() })
			});

			if (res.status === 401) {
				endExpiredSession();
				return;
			}

			if (!res.ok) {
				newSupplierError = await apiErrorMessage(res, 'Failed to create supplier.');
				return;
			}

			const data = await res.json();
			suppliers = [...suppliers, data.supplier];
			supplierOptions = [
				...supplierOptions,
				{ value: data.supplier.id, label: data.supplier.name }
			];
			formSupplierId = String(data.supplier.id);
			newSupplierName = '';
			showNewSupplier = false;
		} catch (err) {
			console.error(err);
			newSupplierError = 'A network error occurred while creating the supplier.';
		} finally {
			creatingSupplier = false;
		}
	}

	async function saveOrder() {
		formError = '';

		if (!formSupplierId) {
			formError = 'Choose a supplier.';
			return;
		}

		const lines = [];
		for (const line of formLines) {
			if (!line.itemId) continue;
			const quantity = parseUnits(line.quantityOrdered);
			if (quantity === null) {
				formError = 'Every line needs a quantity that is a whole number greater than zero.';
				return;
			}
			const unitCost = line.unitCost.trim() === '' ? null : Number(line.unitCost);
			if (unitCost !== null && (Number.isNaN(unitCost) || unitCost < 0)) {
				formError = 'Unit cost cannot be negative.';
				return;
			}
			lines.push({ itemId: Number(line.itemId), quantityOrdered: quantity, unitCost });
		}

		if (lines.length === 0) {
			formError = 'Add at least one line with an item and a quantity.';
			return;
		}

		saving = true;

		try {
			const path = isEditing ? `/api/purchase-orders/${editingId}` : '/api/purchase-orders';
			const method = isEditing ? 'PUT' : 'POST';

			const res = await apiFetch(path, {
				method,
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({
					supplierId: Number(formSupplierId),
					expectedDate: formExpectedDate || null,
					notes: formNotes.trim() || null,
					lines
				})
			});

			if (res.status === 401) {
				endExpiredSession();
				return;
			}

			if (!res.ok) {
				formError = await apiErrorMessage(res, 'Failed to save purchase order.');
				return;
			}

			closeForm();
			if (!isEditing) page = 1;
			await loadOrders();
		} catch (err) {
			console.error(err);
			formError = 'A network error occurred while saving.';
		} finally {
			saving = false;
		}
	}

	// --- VIEW / RECEIVE ----------------------------------------------------

	let viewingOrder: PurchaseOrderDetail | null = $state(null);
	let viewLoading = $state(false);
	let viewError = $state('');
	let actionError = $state('');
	let actionLoading = $state(false);

	async function viewOrder(id: number) {
		showForm = false;
		viewError = '';
		actionError = '';
		viewLoading = true;
		viewingOrder = null;
		try {
			viewingOrder = await fetchPurchaseOrder(id);
		} catch (err) {
			console.error(err);
			viewError = err instanceof Error ? err.message : 'Failed to load this purchase order.';
		} finally {
			viewLoading = false;
		}
	}

	function closeView() {
		viewingOrder = null;
		receivingLineId = null;
	}

	async function refreshView() {
		if (!viewingOrder) return;
		const id = viewingOrder.id;
		viewingOrder = await fetchPurchaseOrder(id);
	}

	async function placeOrder() {
		if (!viewingOrder) return;
		actionError = '';
		actionLoading = true;
		try {
			const res = await apiFetch(`/api/purchase-orders/${viewingOrder.id}/place`, {
				method: 'POST'
			});

			if (res.status === 401) {
				endExpiredSession();
				return;
			}
			if (!res.ok) {
				actionError = await apiErrorMessage(res, 'Failed to place this purchase order.');
				return;
			}

			await refreshView();
			await loadOrders();
		} catch (err) {
			console.error(err);
			actionError = 'A network error occurred while placing the order.';
		} finally {
			actionLoading = false;
		}
	}

	async function cancelOrder() {
		if (!viewingOrder) return;
		if (!confirm('Cancel this purchase order? This cannot be undone.')) return;

		actionError = '';
		actionLoading = true;
		try {
			const res = await apiFetch(`/api/purchase-orders/${viewingOrder.id}/cancel`, {
				method: 'POST'
			});

			if (res.status === 401) {
				endExpiredSession();
				return;
			}
			if (!res.ok) {
				actionError = await apiErrorMessage(res, 'Failed to cancel this purchase order.');
				return;
			}

			await refreshView();
			await loadOrders();
		} catch (err) {
			console.error(err);
			actionError = 'A network error occurred while cancelling the order.';
		} finally {
			actionLoading = false;
		}
	}

	// Only one line's receive form is open at a time - each purchase order is
	// normally worked one delivery, and so one line, at a time, and this keeps
	// the state below to a handful of fields instead of one set per line.
	let receivingLineId: number | null = $state(null);
	let receiveBinId = $state('');
	let receiveQuantity = $state('');
	let receiveLotNumber = $state('');
	let receiveError = $state('');
	let receiving = $state(false);

	function openReceiveForm(line: PurchaseOrderLine) {
		receivingLineId = line.id;
		receiveBinId = '';
		receiveQuantity = '';
		receiveLotNumber = '';
		receiveError = '';
	}

	function closeReceiveForm() {
		receivingLineId = null;
	}

	async function submitReceive(line: PurchaseOrderLine) {
		if (!viewingOrder) return;
		receiveError = '';

		const units = parseUnits(receiveQuantity);
		if (!receiveBinId || units === null) {
			receiveError = !receiveBinId
				? 'Choose the bin the stock is going into.'
				: 'Enter the number of units as a whole number greater than zero.';
			return;
		}

		receiving = true;

		try {
			const res = await apiFetch(
				`/api/purchase-orders/${viewingOrder.id}/lines/${line.id}/receive`,
				{
					method: 'POST',
					headers: { 'Content-Type': 'application/json' },
					body: JSON.stringify({
						warehouseBinId: Number(receiveBinId),
						quantity: units,
						lotNumber: receiveLotNumber.trim() || null
					})
				}
			);

			if (res.status === 401) {
				endExpiredSession();
				return;
			}

			if (!res.ok) {
				receiveError = await apiErrorMessage(res, 'Failed to receive stock against this line.');
				return;
			}

			receivingLineId = null;
			await refreshView();
			await loadOrders();
		} catch (err) {
			console.error(err);
			receiveError = 'A network error occurred while receiving stock.';
		} finally {
			receiving = false;
		}
	}
</script>

{#if allowed}
	<Sidebar activePage="Purchase Orders" />
	<Header />

	<main class="dashboard-content">
		<div class="page-header">
			<div>
				<h2>Purchase Orders</h2>
				<p>What is on order, what has arrived, and what is still short.</p>
			</div>
			{#if !showForm}
				<button class="btn-solid" onclick={openCreateForm}>+ New Purchase Order</button>
			{/if}
		</div>

		{#if errorMsg}
			<div class="alert alert-error">{errorMsg}</div>
		{/if}

		{#if showForm}
			<div class="panel form-panel">
				<h3>{isEditing ? `Edit Purchase Order #${editingId}` : 'Create Purchase Order'}</h3>
				<form
					onsubmit={(e) => {
						e.preventDefault();
						saveOrder();
					}}
					class="form-grid"
				>
					<div class="input-row three">
						<div class="supplier-picker">
							<SelectField
								id="supplier"
								label="SUPPLIER"
								options={supplierOptions}
								bind:value={formSupplierId}
								placeholder="Select a supplier"
								emptyLabel={isLoadingPickers ? 'Loading...' : 'No suppliers yet'}
								required={true}
							/>
							{#if !showNewSupplier}
								<button type="button" class="link-btn" onclick={() => (showNewSupplier = true)}>
									+ New supplier
								</button>
							{:else}
								<div class="inline-supplier">
									<input
										type="text"
										placeholder="Supplier name"
										bind:value={newSupplierName}
										disabled={creatingSupplier}
									/>
									<button
										type="button"
										class="btn-outline small"
										onclick={createSupplierInline}
										disabled={creatingSupplier}
									>
										{creatingSupplier ? 'Adding…' : 'Add'}
									</button>
									<button
										type="button"
										class="link-btn"
										onclick={() => (showNewSupplier = false)}
										disabled={creatingSupplier}>Cancel</button
									>
								</div>
								{#if newSupplierError}
									<span class="field-error">{newSupplierError}</span>
								{/if}
							{/if}
						</div>
						<InputField
							id="expected-date"
							type="date"
							label="EXPECTED DATE"
							placeholder=""
							bind:value={formExpectedDate}
						/>
						<InputField id="notes" label="NOTES" placeholder="Optional" bind:value={formNotes} />
					</div>

					<div class="lines-editor">
						<div class="lines-header">
							<span>ITEM</span>
							<span>QUANTITY</span>
							<span>UNIT COST</span>
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
								<input
									type="number"
									min="1"
									step="1"
									placeholder="Qty"
									bind:value={line.quantityOrdered}
								/>
								<input
									type="number"
									min="0"
									step="0.01"
									placeholder="Optional"
									bind:value={line.unitCost}
								/>
								<button
									type="button"
									class="btn-icon delete"
									onclick={() => removeLine(index)}
									disabled={formLines.length <= 1}
									title="Remove line"
								>
									🗑️
								</button>
							</div>
						{/each}
						<button type="button" class="link-btn" onclick={addLine}>+ Add line</button>
					</div>

					{#if formError}
						<div class="alert alert-error">{formError}</div>
					{/if}

					<div class="actions">
						<button type="button" class="btn-outline" onclick={closeForm}>Cancel</button>
						<div class="submit-wrap">
							<Button
								type="submit"
								text={isEditing ? 'Update Order' : 'Save Draft'}
								isLoading={saving}
								loadingText="Saving…"
							/>
						</div>
					</div>
				</form>
			</div>
		{/if}

		{#if viewingOrder || viewLoading}
			<div class="panel view-panel">
				{#if viewLoading}
					<p class="empty-state">Loading purchase order…</p>
				{:else if viewingOrder}
					<div class="view-header">
						<div>
							<h3>
								Purchase Order #{viewingOrder.id}
								<span class="badge {statusBadgeClass(viewingOrder.status)}"
									>{statusLabel(viewingOrder.status)}</span
								>
							</h3>
							<p class="subtitle">{viewingOrder.supplierName ?? 'Unknown supplier'}</p>
						</div>
						<button type="button" class="btn-outline" onclick={closeView}>Close</button>
					</div>

					<div class="view-meta">
						<div>
							<span class="meta-label">Created</span>{formatDateTime(viewingOrder.createdAt)}
						</div>
						<div><span class="meta-label">By</span>{viewingOrder.createdBy}</div>
						<div>
							<span class="meta-label">Ordered</span>{formatDateTime(viewingOrder.orderedAt)}
						</div>
						<div>
							<span class="meta-label">Expected</span>{formatDate(viewingOrder.expectedDate)}
						</div>
						<div>
							<span class="meta-label">Received</span>{formatDateTime(viewingOrder.receivedAt)}
						</div>
					</div>

					{#if viewingOrder.notes}
						<p class="notes">{viewingOrder.notes}</p>
					{/if}

					{#if actionError}
						<div class="alert alert-error">{actionError}</div>
					{/if}

					<table class="data-table">
						<thead>
							<tr>
								<th>ITEM</th>
								<th>ORDERED</th>
								<th>RECEIVED</th>
								<th>REMAINING</th>
								<th>UNIT COST</th>
								<th class="text-right">ACTIONS</th>
							</tr>
						</thead>
						<tbody>
							{#each viewingOrder.lines as line (line.id)}
								<tr>
									<td>
										<strong>{line.itemName ?? `Item #${line.itemId}`}</strong>
										{#if line.itemSku}<span class="text-muted"> · {line.itemSku}</span>{/if}
									</td>
									<td>{line.quantityOrdered.toLocaleString()}</td>
									<td>{line.quantityReceived.toLocaleString()}</td>
									<td>
										{line.quantityRemaining.toLocaleString()}
										{#if isLineOutstanding(line) && line.quantityReceived > 0}
											<span class="badge amber">SHORT</span>
										{/if}
									</td>
									<td class="text-muted"
										>{line.unitCost !== null ? line.unitCost.toFixed(2) : '—'}</td
									>
									<td class="text-right">
										{#if (viewingOrder.status === 'Ordered' || viewingOrder.status === 'PartiallyReceived') && isLineOutstanding(line)}
											<button class="btn-outline small" onclick={() => openReceiveForm(line)}>
												Receive
											</button>
										{/if}
									</td>
								</tr>
								{#if receivingLineId === line.id}
									<tr class="receive-row">
										<td colspan="6">
											<form
												class="receive-form"
												onsubmit={(e) => {
													e.preventDefault();
													submitReceive(line);
												}}
											>
												<select bind:value={receiveBinId} disabled={isLoadingPickers}>
													<option value="" disabled>
														{isLoadingPickers ? 'Loading...' : 'Select a bin'}
													</option>
													{#each binOptions as option (option.value)}
														<option value={String(option.value)}>{option.label}</option>
													{/each}
												</select>
												<input
													type="number"
													min="1"
													step="1"
													max={line.quantityRemaining}
													placeholder={`Up to ${line.quantityRemaining}`}
													bind:value={receiveQuantity}
												/>
												<input
													type="text"
													placeholder="Lot/batch (if tracked)"
													bind:value={receiveLotNumber}
												/>
												<button type="submit" class="btn-solid small" disabled={receiving}>
													{receiving ? 'Receiving…' : 'Confirm'}
												</button>
												<button
													type="button"
													class="link-btn"
													onclick={closeReceiveForm}
													disabled={receiving}>Cancel</button
												>
											</form>
											{#if receiveError}
												<span class="field-error">{receiveError}</span>
											{/if}
										</td>
									</tr>
								{/if}
							{/each}
						</tbody>
					</table>

					<div class="view-actions">
						{#if viewingOrder.status === 'Draft'}
							<button
								type="button"
								class="btn-outline"
								onclick={() => viewingOrder && openEditForm(viewingOrder)}>Edit</button
							>
							<button
								type="button"
								class="btn-outline"
								onclick={cancelOrder}
								disabled={actionLoading}>Cancel Order</button
							>
							<button type="button" class="btn-solid" onclick={placeOrder} disabled={actionLoading}>
								{actionLoading ? 'Placing…' : 'Place Order'}
							</button>
						{:else if viewingOrder.status === 'Ordered' || viewingOrder.status === 'PartiallyReceived'}
							<button
								type="button"
								class="btn-outline"
								onclick={cancelOrder}
								disabled={actionLoading}>Cancel Order</button
							>
						{/if}
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
					options={PURCHASE_ORDER_STATUSES.map((s) => ({ value: s, label: statusLabel(s) }))}
					bind:value={statusFilter}
					placeholder="All statuses"
					required={false}
				/>
				<SelectField
					id="supplier-filter"
					label="SUPPLIER"
					options={supplierOptions}
					bind:value={supplierFilter}
					placeholder="All suppliers"
					emptyLabel="No suppliers yet"
					required={false}
				/>
				<div class="filter-actions">
					<button class="btn-outline" onclick={applyFilters}>Apply</button>
					<button
						class="btn-outline"
						onclick={() => {
							statusFilter = '';
							supplierFilter = '';
							applyFilters();
						}}>Clear</button
					>
				</div>
			</div>

			<table class="data-table">
				<thead>
					<tr>
						<th>ID</th>
						<th>SUPPLIER</th>
						<th>STATUS</th>
						<th>CREATED</th>
						<th>EXPECTED</th>
						<th>ORDERED / RECEIVED</th>
						<th class="text-right">ACTIONS</th>
					</tr>
				</thead>
				<tbody>
					{#if isLoading}
						<tr><td colspan="7" class="empty-state">Loading purchase orders...</td></tr>
					{:else if orders.length === 0}
						<tr
							><td colspan="7" class="empty-state">No purchase orders found. Create one above.</td
							></tr
						>
					{:else}
						{#each orders as order (order.id)}
							<tr>
								<td class="text-muted">#{order.id}</td>
								<td><strong>{order.supplierName ?? 'Unknown supplier'}</strong></td>
								<td
									><span class="badge {statusBadgeClass(order.status)}"
										>{statusLabel(order.status)}</span
									></td
								>
								<td class="text-muted">{formatDate(order.createdAt)}</td>
								<td class="text-muted">{formatDate(order.expectedDate)}</td>
								<td
									>{order.totalReceived.toLocaleString()} / {order.totalOrdered.toLocaleString()}</td
								>
								<td class="text-right">
									<button class="btn-outline small" onclick={() => viewOrder(order.id)}>View</button
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
						Showing {orders.length} of {totalCount} order{totalCount === 1 ? '' : 's'} &middot; page {page}
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
	.input-row {
		display: grid;
		gap: 1rem;
		margin-bottom: 1.5rem;
		align-items: start;
	}
	.input-row.three {
		grid-template-columns: repeat(3, 1fr);
	}
	.supplier-picker .inline-supplier {
		display: flex;
		gap: 0.5rem;
		margin-top: -0.75rem;
		margin-bottom: 0.5rem;
	}
	.supplier-picker .inline-supplier input {
		flex: 1;
		padding: 0.5rem 0.75rem;
		border: 1px solid #cbd5e1;
		border-radius: 6px;
		font-size: 0.85rem;
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
		grid-template-columns: 2fr 1fr 1fr auto;
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
		width: 150px;
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

	.badge {
		padding: 0.25rem 0.5rem;
		border-radius: 4px;
		font-size: 0.7rem;
		font-weight: 700;
		letter-spacing: 0.3px;
		margin-left: 0.5rem;
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
		grid-template-columns: repeat(5, 1fr);
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
	.notes {
		background: #f8fafc;
		border: 1px solid #e2e8f0;
		border-radius: 6px;
		padding: 0.75rem 1rem;
		font-size: 0.85rem;
		color: #475569;
		margin-bottom: 1.5rem;
	}
	.view-actions {
		display: flex;
		justify-content: flex-end;
		gap: 0.75rem;
		margin-top: 1.5rem;
	}

	.receive-row td {
		background: #f8fafc;
		padding: 1rem;
	}
	.receive-form {
		display: flex;
		gap: 0.75rem;
		align-items: center;
	}
	.receive-form select,
	.receive-form input {
		padding: 0.5rem 0.75rem;
		border: 1px solid #cbd5e1;
		border-radius: 6px;
		font-family: inherit;
		font-size: 0.85rem;
	}
	.receive-form input[type='number'] {
		width: 120px;
	}
	.receive-form input[type='text'] {
		width: 180px;
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
		grid-template-columns: 1fr 1fr auto;
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
