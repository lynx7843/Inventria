<script lang="ts">
  import Sidebar from '$lib/components/shared/Sidebar.svelte';
  import Header from '$lib/components/shared/Header.svelte';
  import ReceiveStockForm from '$lib/components/employee/ReceiveStockForm.svelte';
  import PickStockForm from '$lib/components/employee/PickStockForm.svelte';
  import RelocateStockForm from '$lib/components/employee/RelocateStockForm.svelte';
  import { onMount } from 'svelte';
  import { apiFetch, apiErrorMessage } from '$lib/api';
  import { endExpiredSession, requireSession } from '$lib/auth';
  import { fetchItems, type Item } from '$lib/inventory';

  // Gates the markup below. No role list: the stock movements on this page are
  // open to Admins as well, so this only requires that someone is signed in.
  let allowed = $state(false);

  // State variables for our data
  let inventoryItems: Item[] = $state([]);
  let isLoading = $state(true);
  let errorMsg = $state('');

  // The four counters across the top. Zeros until the API answers, rather than
  // the invented figures that used to sit here - a brand new warehouse really
  // does have nothing in it, and saying so beats saying 12,482.
  let stats = $state({
    unitsOnHand: 0,
    skusTracked: 0,
    receivedToday: 0,
    pickedToday: 0
  });

  // Track which transaction form is visible
  let activeTab = $state('receive');

  // Fetch data as soon as the page loads
  onMount(async () => {
    if (!requireSession()) return;
    allowed = true;

    try {
      const [items, statsResponse] = await Promise.all([
        fetchItems(),
        apiFetch('/api/dashboard/employee')
      ]);

      if (statsResponse.status === 401) {
        // Session is missing or expired, kick them back to login
        endExpiredSession();
        return;
      }

      if (!statsResponse.ok) {
        throw new Error(await apiErrorMessage(statsResponse, 'Failed to load dashboard totals.'));
      }

      inventoryItems = items;
      stats = await statsResponse.json();
    } catch (err) {
      errorMsg = err instanceof Error ? err.message : 'Unknown error occurred.';
    } finally {
      isLoading = false;
    }
  });
</script>

{#if allowed}
<Sidebar activePage="Dashboard" />
<Header />

<main class="dashboard-content">
  <div class="page-header">
    <div>
      <h2>Employee Dashboard</h2>
      <p>Welcome back. Here's what's happening in the warehouse today.</p>
    </div>
    <div class="actions">
      <button class="btn-outline">Scan Item</button>
      <button class="btn-solid">Update Stock</button>
    </div>
  </div>

  <!-- Every figure here is one the database can answer. The two that used to be
       here and cannot be - a low-stock count, which needs a reorder level no
       item carries, and an "efficiency rate", which was never defined as
       anything - were removed rather than approximated. -->
  <div class="stats-grid">
    <div class="stat-card">
      <div class="icon-box">📦</div>
      <p class="subtext">UNITS ON HAND</p>
      <div class="value">{stats.unitsOnHand.toLocaleString()}</div>
    </div>
    <div class="stat-card">
      <div class="icon-box">🏷️</div>
      <p class="subtext">SKUS TRACKED</p>
      <div class="value">{stats.skusTracked.toLocaleString()}</div>
    </div>
    <div class="stat-card">
      <div class="icon-box success">✔️</div>
      <p class="subtext">RECEIVED TODAY</p>
      <div class="value">{stats.receivedToday.toLocaleString()}</div>
    </div>
    <div class="stat-card efficiency">
      <p class="subtext text-white">PICKED TODAY</p>
      <div class="value text-white">{stats.pickedToday.toLocaleString()}</div>
    </div>
  </div>

  <!-- TRANSACTION CONTROLS -->
  <div class="transaction-controls">
    <button
      class="tab-btn"
      class:active={activeTab === 'receive'}
      on:click={() => activeTab = 'receive'}>
      ↓ Receive Stock
    </button>
    <button
      class="tab-btn"
      class:active={activeTab === 'pick'}
      on:click={() => activeTab = 'pick'}>
      ↑ Pick Order
    </button>
    <button
      class="tab-btn"
      class:active={activeTab === 'relocate'}
      on:click={() => activeTab = 'relocate'}>
      ⇄ Relocate
    </button>
  </div>

  <!-- CONDITIONALLY RENDER THE ACTIVE FORM -->
  {#if activeTab === 'receive'}
    <ReceiveStockForm />
  {:else if activeTab === 'pick'}
    <PickStockForm />
  {:else if activeTab === 'relocate'}
    <RelocateStockForm />
  {/if}

  <div class="panel">
    <div class="panel-header">
      <h3>Inventory Overview</h3>
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
          <tr>
            <td colspan="5" style="text-align: center; padding: 2rem; color: #64748b;">
              Loading inventory database...
            </td>
          </tr>
        {:else if errorMsg}
          <tr>
            <td colspan="5" style="text-align: center; padding: 2rem; color: #ef4444;">
              {errorMsg}
            </td>
          </tr>
        {:else if inventoryItems.length === 0}
          <tr>
            <td colspan="5" style="text-align: center; padding: 2rem; color: #64748b;">
              No items found in the master list.
            </td>
          </tr>
        {:else}
          <!-- Loop through the SQL data -->
          {#each inventoryItems as item}
            <tr>
              <td><strong>{item.name}</strong></td>
              <td>{item.sku}</td>
              <td>{item.category}</td>
              <td>{item.quantityOnHand.toLocaleString()}</td>
              <!-- The badge said ACTIVE for everything, which described the row
                   rather than the stock. What a picker needs to know is whether
                   there is any to pick. -->
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
  </div>
</main>
{/if}

<style>
  .dashboard-content { margin-left: 250px; padding: 2rem; background: #f8fafc; min-height: calc(100vh - 70px); }
  .page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 2rem; }
  .page-header h2 { margin: 0 0 0.25rem 0; color: #0f172a; }
  .page-header p { margin: 0; color: #64748b; }
  
  .btn-outline { background: #dcfce7; color: #166534; border: none; padding: 0.5rem 1rem; border-radius: 6px; font-weight: 600; cursor: pointer; }
  .btn-solid { background: #0b6b36; color: white; border: none; padding: 0.5rem 1rem; border-radius: 6px; font-weight: 600; cursor: pointer; margin-left: 0.5rem; }

  .stats-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 1.5rem; margin-bottom: 2rem; }
  .stat-card { background: white; padding: 1.5rem; border-radius: 8px; border: 1px solid #e2e8f0; position: relative; }
  .icon-box { position: absolute; top: 1.5rem; right: 1.5rem; background: #f1f5f9; padding: 0.5rem; border-radius: 8px; }
  .icon-box.success { background: #dcfce7; }
  .stat-card .subtext { font-size: 0.75rem; color: #64748b; letter-spacing: 0.5px; margin: 0 0 0.5rem 0; padding-top: 1rem; }
  .stat-card .value { font-size: 2.5rem; font-weight: 700; color: #0f172a; margin: 0; }
  .efficiency { background: #0b6b36; border-color: #0b6b36; }
  .text-white { color: white !important; }

  .transaction-controls { display: flex; gap: 0.5rem; margin-bottom: 1rem; border-bottom: 2px solid #e2e8f0; padding-bottom: 0.5rem; }
  .tab-btn { background: none; border: none; padding: 0.5rem 1rem; font-weight: 600; color: #64748b; cursor: pointer; border-radius: 6px; transition: all 0.2s; }
  .tab-btn:hover { background: #f1f5f9; color: #0f172a; }
  .tab-btn.active { background: #dcfce7; color: #0b6b36; }

  .panel { background: white; padding: 1.5rem; border-radius: 8px; border: 1px solid #e2e8f0; }
  .panel-header h3 { margin: 0 0 1.5rem 0; font-size: 1.1rem; color: #0f172a; }
  
  .data-table { width: 100%; border-collapse: collapse; text-align: left; }
  .data-table th { padding: 1rem; border-bottom: 2px solid #e2e8f0; color: #64748b; font-size: 0.75rem; letter-spacing: 0.5px; }
  .data-table td { padding: 1rem; border-bottom: 1px solid #e2e8f0; font-size: 0.9rem; color: #475569; }
  .data-table td strong { color: #0f172a; }
  
  .badge { padding: 0.25rem 0.75rem; border-radius: 20px; font-size: 0.75rem; font-weight: 600; }
  .badge.in-stock { background: #dcfce7; color: #166534; }
  .badge.out-stock { background: #e2e8f0; color: #475569; }
</style>