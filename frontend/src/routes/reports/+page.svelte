<script lang="ts">
  import Sidebar from '$lib/components/shared/Sidebar.svelte';
  import Header from '$lib/components/shared/Header.svelte';
  import { onMount } from 'svelte';
  import { requireSession } from '$lib/auth';
  import { fetchAllItems, type Item } from '$lib/inventory';

  // Open to anyone signed in, same as Inventory and Bins - reports on the
  // catalogue aren't an Admin secret.
  let allowed = $state(false);

  let items: Item[] = $state([]);
  let isLoading = $state(true);
  let errorMsg = $state('');

  // The report reads the whole catalogue rather than one page of it, so every
  // figure below - the totals, the category split, the table - reflects all
  // of it and not just whatever page loaded first.
  let searchTerm = $state('');
  let categoryFilter = $state('All');

  const pageSize = 10;
  let page = $state(1);

  onMount(async () => {
    if (!requireSession()) return;
    allowed = true;

    try {
      items = await fetchAllItems();
    } catch (err) {
      errorMsg = err instanceof Error ? err.message : 'Unknown error occurred.';
    } finally {
      isLoading = false;
    }
  });

  // Every number on this page is derived from the fetched items - nothing here
  // is invented. There's no reorder level anywhere in the schema, so "out of
  // stock" (quantityOnHand === 0) is the only stock-health figure that can
  // honestly be reported without one.
  let totalUnits = $derived(items.reduce((sum, item) => sum + item.quantityOnHand, 0));
  let outOfStockCount = $derived(items.filter((item) => item.quantityOnHand === 0).length);
  let categories = $derived(Array.from(new Set(items.map((item) => item.category))).sort());

  type CategoryCount = { category: string; count: number; units: number };
  let categoryBreakdown = $derived<CategoryCount[]>(
    categories
      .map((category) => {
        const inCategory = items.filter((item) => item.category === category);
        return {
          category,
          count: inCategory.length,
          units: inCategory.reduce((sum, item) => sum + item.quantityOnHand, 0)
        };
      })
      .sort((a, b) => b.units - a.units)
  );

  let filteredItems = $derived(
    items.filter((item) => {
      const matchesCategory = categoryFilter === 'All' || item.category === categoryFilter;
      const term = searchTerm.trim().toLowerCase();
      const matchesSearch =
        term === '' ||
        item.name.toLowerCase().includes(term) ||
        item.sku.toLowerCase().includes(term);
      return matchesCategory && matchesSearch;
    })
  );

  // Filtering changes the result set out from under whatever page you were on;
  // rather than leaving you stranded on a now-empty page 4, snap back to 1.
  $effect(() => {
    filteredItems;
    page = 1;
  });

  let totalPages = $derived(Math.max(1, Math.ceil(filteredItems.length / pageSize)));
  let pagedItems = $derived(filteredItems.slice((page - 1) * pageSize, page * pageSize));

  function goToPage(next: number) {
    if (next < 1 || next > totalPages) return;
    page = next;
  }

  // Exports exactly what's on screen - the filtered set, not the whole
  // catalogue - so the file matches what you were just looking at.
  function exportCsv() {
    const rows = [
      ['SKU', 'Name', 'Category', 'Quantity On Hand', 'Status'],
      ...filteredItems.map((item) => [
        item.sku,
        item.name,
        item.category,
        String(item.quantityOnHand),
        item.quantityOnHand === 0 ? 'Out of Stock' : 'In Stock'
      ])
    ];

    const csv = rows
      .map((row) => row.map((cell) => `"${cell.replace(/"/g, '""')}"`).join(','))
      .join('\r\n');

    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
    const link = document.createElement('a');

    link.href = url;
    link.download = `inventria-stock-report-${new Date().toISOString().slice(0, 10)}.csv`;
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
      <p>A snapshot of the current catalogue - stock levels, categories, and status at a glance.</p>
    </div>
    <div class="actions">
      <button class="btn-outline" onclick={exportCsv} disabled={filteredItems.length === 0}>Export CSV</button>
      <button class="btn-solid" onclick={() => window.print()}>Print Report</button>
    </div>
  </div>

  <div class="stats-grid">
    <div class="stat-card">
      <h4>TOTAL SKUS</h4>
      <div class="value">{items.length.toLocaleString()}</div>
      <p class="subtext">Distinct items tracked</p>
    </div>
    <div class="stat-card">
      <h4>TOTAL UNITS ON HAND</h4>
      <div class="value">{totalUnits.toLocaleString()}</div>
      <p class="subtext">Across every bin</p>
    </div>
    <div class="stat-card">
      <h4>OUT OF STOCK</h4>
      <div class="value" class:critical={outOfStockCount > 0}>{outOfStockCount.toLocaleString()}</div>
      <p class="subtext">SKUs at zero units</p>
    </div>
    <div class="stat-card categories-card">
      <h4>CATEGORIES</h4>
      <div class="value text-white">{categories.length}</div>
      <p class="text-white">Distinct categories in the catalogue</p>
    </div>
  </div>

  <div class="panel">
    <div class="panel-header">
      <h3>Stock by Category</h3>
    </div>
    {#if isLoading}
      <p class="empty-state">Loading report data...</p>
    {:else if categoryBreakdown.length === 0}
      <p class="empty-state">No items found in the master list.</p>
    {:else}
      <div class="category-list">
        {#each categoryBreakdown as row}
          <div class="category-row">
            <div class="category-label">
              <span>{row.category}</span>
              <span class="sub">{row.count} SKU{row.count === 1 ? '' : 's'} &middot; {row.units.toLocaleString()} units</span>
            </div>
            <div class="bar-track">
              <div
                class="bar-fill"
                style="width: {totalUnits === 0 ? 0 : Math.round((row.units / totalUnits) * 100)}%"
              ></div>
            </div>
          </div>
        {/each}
      </div>
    {/if}
  </div>

  <div class="panel mt-1">
    <div class="panel-header">
      <h3>Inventory Report</h3>
      <div class="filters">
        <input
          class="search-input"
          type="text"
          placeholder="Search by name or SKU..."
          bind:value={searchTerm}
        />
        <select class="category-select" bind:value={categoryFilter}>
          <option value="All">All Categories</option>
          {#each categories as category}
            <option value={category}>{category}</option>
          {/each}
        </select>
      </div>
    </div>

    <table class="data-table">
      <thead>
        <tr>
          <th>ITEM NAME</th>
          <th>SKU</th>
          <th>CATEGORY</th>
          <th>QUANTITY</th>
          <th>STATUS</th>
        </tr>
      </thead>
      <tbody>
        {#if isLoading}
          <tr><td colspan="5" class="empty-state">Loading inventory database...</td></tr>
        {:else if errorMsg}
          <tr><td colspan="5" class="empty-state error">{errorMsg}</td></tr>
        {:else if pagedItems.length === 0}
          <tr><td colspan="5" class="empty-state">No items match your search.</td></tr>
        {:else}
          {#each pagedItems as item}
            <tr>
              <td><strong>{item.name}</strong></td>
              <td>{item.sku}</td>
              <td>{item.category}</td>
              <td>{item.quantityOnHand.toLocaleString()}</td>
              <td>
                {#if item.quantityOnHand > 0}
                  <span class="badge in-stock">IN STOCK</span>
                {:else}
                  <span class="badge out-stock">OUT OF STOCK</span>
                {/if}
              </td>
            </tr>
          {/each}
        {/if}
      </tbody>
    </table>

    {#if filteredItems.length > 0}
      <div class="pager">
        <span class="pager-status">
          Showing {pagedItems.length} of {filteredItems.length} item{filteredItems.length === 1 ? '' : 's'} &middot; page {page} of {totalPages}
        </span>
        <div class="pager-buttons">
          <button class="pager-btn" onclick={() => goToPage(page - 1)} disabled={page <= 1}>Previous</button>
          <button class="pager-btn" onclick={() => goToPage(page + 1)} disabled={page >= totalPages}>Next</button>
        </div>
      </div>
    {/if}
  </div>
</main>
{/if}

<style>
  .dashboard-content { margin-left: 250px; padding: 2rem; background: #f8fafc; min-height: calc(100vh - 70px); }
  .page-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 1.5rem; }
  .page-header h2 { margin: 0 0 0.25rem 0; color: #0f172a; }
  .page-header p { margin: 0; color: #64748b; }
  .actions { display: flex; gap: 0.75rem; }

  .btn-outline { background: white; border: 1px solid #cbd5e1; padding: 0.6rem 1.1rem; border-radius: 6px; font-weight: 500; cursor: pointer; color: #334155; }
  .btn-outline:hover:not(:disabled) { background: #f1f5f9; }
  .btn-outline:disabled { color: #94a3b8; cursor: not-allowed; }
  .btn-solid { background: #0b6b36; color: white; border: none; padding: 0.6rem 1.1rem; border-radius: 6px; font-weight: 500; cursor: pointer; }
  .btn-solid:hover { background: #095028; }

  .stats-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 1.5rem; margin-bottom: 1.5rem; }
  .stat-card { background: white; padding: 1.5rem; border-radius: 8px; border: 1px solid #e2e8f0; }
  .stat-card h4 { margin: 0 0 1rem 0; font-size: 0.75rem; color: #64748b; letter-spacing: 0.5px; }
  .stat-card .value { font-size: 2rem; font-weight: 700; color: #0f172a; margin-bottom: 0.5rem; }
  .stat-card .value.critical { color: #dc2626; }
  .stat-card .subtext { margin: 0; font-size: 0.8rem; color: #64748b; }
  .categories-card { background: #0b6b36; border-color: #0b6b36; }
  .text-white { color: white !important; }

  .panel { background: white; padding: 1.5rem; border-radius: 8px; border: 1px solid #e2e8f0; }
  .panel-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem; gap: 1rem; flex-wrap: wrap; }
  .panel-header h3 { margin: 0; font-size: 1.1rem; color: #0f172a; }
  .mt-1 { margin-top: 1.5rem; }

  .category-list { display: flex; flex-direction: column; gap: 1rem; }
  .category-row { display: flex; flex-direction: column; gap: 0.4rem; }
  .category-label { display: flex; justify-content: space-between; align-items: baseline; font-size: 0.9rem; color: #0f172a; font-weight: 600; }
  .category-label .sub { font-size: 0.8rem; color: #64748b; font-weight: 500; }
  .bar-track { height: 8px; background: #f1f5f9; border-radius: 99px; overflow: hidden; }
  .bar-fill { height: 100%; background: #0b6b36; border-radius: 99px; }

  .filters { display: flex; gap: 0.5rem; }
  .search-input { border: 1px solid #cbd5e1; border-radius: 6px; padding: 0.5rem 0.75rem; font-size: 0.85rem; color: #334155; font-family: inherit; min-width: 220px; }
  .search-input:focus { outline: none; border-color: #0b6b36; }
  .category-select { border: 1px solid #cbd5e1; border-radius: 6px; padding: 0.5rem 0.75rem; font-size: 0.85rem; color: #334155; font-family: inherit; background: white; }

  .data-table { width: 100%; border-collapse: collapse; text-align: left; }
  .data-table th { padding: 1rem; border-bottom: 2px solid #e2e8f0; color: #64748b; font-size: 0.75rem; letter-spacing: 0.5px; }
  .data-table td { padding: 1rem; border-bottom: 1px solid #e2e8f0; font-size: 0.9rem; color: #475569; }
  .data-table td strong { color: #0f172a; }

  .pager { display: flex; justify-content: space-between; align-items: center; margin-top: 1rem; }
  .pager-status { font-size: 0.8rem; color: #64748b; }
  .pager-buttons { display: flex; gap: 0.5rem; }
  .pager-btn { background: white; color: #475569; border: 1px solid #cbd5e1; padding: 0.5rem 1rem; border-radius: 6px; font-weight: 500; cursor: pointer; }
  .pager-btn:hover:not(:disabled) { background: #f1f5f9; }
  .pager-btn:disabled { color: #94a3b8; border-color: #e2e8f0; cursor: not-allowed; }

  .badge { padding: 0.25rem 0.75rem; border-radius: 20px; font-size: 0.75rem; font-weight: 600; }
  .badge.in-stock { background: #dcfce7; color: #166534; }
  .badge.out-stock { background: #e2e8f0; color: #475569; }

  .empty-state { text-align: center; padding: 2rem; color: #64748b; font-style: italic; }
  .empty-state.error { color: #ef4444; font-style: normal; }
</style>
