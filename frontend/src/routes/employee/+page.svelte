<script lang="ts">
  import Sidebar from '$lib/components/shared/Sidebar.svelte';
  import Header from '$lib/components/shared/Header.svelte';
</script>

<Sidebar activePage="Dashboard" />
<Header userName="Alice Smith" role="Inventory Manager" />

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

  <div class="stats-grid">
    <div class="stat-card">
      <div class="icon-box">📦</div>
      <p class="subtext">TOTAL ITEMS</p>
      <div class="value">12,482</div>
    </div>
    <div class="stat-card">
      <div class="icon-box alert">⚠️</div>
      <p class="subtext">LOW STOCK ALERTS</p>
      <div class="value text-red">14</div>
    </div>
    <div class="stat-card">
      <div class="icon-box success">✔️</div>
      <p class="subtext">ITEMS RECEIVED TODAY</p>
      <div class="value">342</div>
    </div>
    <div class="stat-card efficiency">
      <p class="subtext text-white">EFFICIENCY RATE</p>
      <div class="value text-white">98.4%</div>
      <div class="progress-bar"><div class="fill" style="width: 98.4%"></div></div>
    </div>
  </div>

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
        <tr>
          <td><strong>Industrial Fan Alpha</strong></td>
          <td>SKU-20445-IF</td>
          <td>Electronics</td>
          <td>142 units</td>
          <td><span class="badge in-stock">IN-STOCK</span></td>
        </tr>
        <tr>
          <td><strong>Heavy Duty Cable (50m)</strong></td>
          <td>SKU-99210-CB</td>
          <td>Electrical</td>
          <td>12 units</td>
          <td><span class="badge low-stock">LOW STOCK</span></td>
        </tr>
        <tr>
          <td><strong>Steel Wrench Set</strong></td>
          <td>SKU-31120-TW</td>
          <td>Tools</td>
          <td>58 units</td>
          <td><span class="badge in-stock">IN-STOCK</span></td>
        </tr>
        <tr>
          <td><strong>Pallet Wrap Stretch Film</strong></td>
          <td>SKU-88211-SF</td>
          <td>Packaging</td>
          <td>0 units</td>
          <td><span class="badge out-stock">OUT OF STOCK</span></td>
        </tr>
      </tbody>
    </table>
  </div>
</main>

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
  .icon-box.alert { background: #fee2e2; }
  .icon-box.success { background: #dcfce7; }
  .stat-card .subtext { font-size: 0.75rem; color: #64748b; letter-spacing: 0.5px; margin: 0 0 0.5rem 0; padding-top: 1rem; }
  .stat-card .value { font-size: 2.5rem; font-weight: 700; color: #0f172a; margin: 0; }
  .text-red { color: #ef4444 !important; }
  .efficiency { background: #0b6b36; border-color: #0b6b36; }
  .text-white { color: white !important; }
  .progress-bar { width: 100%; height: 4px; background: rgba(255,255,255,0.3); border-radius: 2px; margin-top: 1rem; }
  .progress-bar .fill { height: 100%; background: white; }

  .panel { background: white; padding: 1.5rem; border-radius: 8px; border: 1px solid #e2e8f0; }
  .panel-header h3 { margin: 0 0 1.5rem 0; font-size: 1.1rem; color: #0f172a; }
  
  .data-table { width: 100%; border-collapse: collapse; text-align: left; }
  .data-table th { padding: 1rem; border-bottom: 2px solid #e2e8f0; color: #64748b; font-size: 0.75rem; letter-spacing: 0.5px; }
  .data-table td { padding: 1rem; border-bottom: 1px solid #e2e8f0; font-size: 0.9rem; color: #475569; }
  .data-table td strong { color: #0f172a; }
  
  .badge { padding: 0.25rem 0.75rem; border-radius: 20px; font-size: 0.75rem; font-weight: 600; }
  .badge.in-stock { background: #dcfce7; color: #166534; }
  .badge.low-stock { background: #fee2e2; color: #991b1b; }
  .badge.out-stock { background: #e2e8f0; color: #475569; }
</style>