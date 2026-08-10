<script lang="ts">
  import Sidebar from '$lib/components/shared/Sidebar.svelte';
  import Header from '$lib/components/shared/Header.svelte';
  import { onMount } from 'svelte';
  import { apiFetch, apiErrorMessage } from '$lib/api';
  import { endExpiredSession, requireSession } from '$lib/auth';

  // Gates the markup below: nothing renders until the guard confirms an Admin,
  // so a redirect never flashes the dashboard on its way out.
  let allowed = $state(false);

  // The shapes GET /api/dashboard/admin and GET /api/users answer with. Spelled
  // out because an untyped $state([]) infers never[], which makes every field
  // read off a row an error.
  type CategoryCount = { category: string; count: number };

  type ActivityLog = {
    transactionType: string;
    quantityChanged: number;
    timestamp: string;
    performedBy: string;
    itemName: string;
    warehouseBinId: number | null;
  };

  type Account = { id: number; username: string; role: string };

  let stats: {
    totalUsers: number;
    totalStockQuantity: number;
    monthlyThroughput: number;
    distribution: CategoryCount[];
    totalUniqueItems: number;
    recentActivity: ActivityLog[];
  } = $state({
    totalUsers: 0,
    totalStockQuantity: 0,
    monthlyThroughput: 0,
    distribution: [],
    totalUniqueItems: 0,
    recentActivity: []
  });

  // The accounts that actually exist. The table below used to be three invented
  // people with invented email addresses, sitting under real figures - which
  // makes every real figure on the page look invented too.
  let users: Account[] = $state([]);

  let isLoading = $state(true);
  let errorMsg = $state('');

  onMount(async () => {
    if (!requireSession(['Admin'])) return;
    allowed = true;

    try {
      // Both are Admin-only and this page is already behind that check, so they
      // go out together rather than one after the other.
      const [response, usersResponse] = await Promise.all([
        apiFetch('/api/dashboard/admin'),
        apiFetch('/api/users')
      ]);

      if (response.status === 401 || usersResponse.status === 401) {
        // Token is missing or expired, kick them back to login
        endExpiredSession();
        return;
      }

      if (!response.ok) throw new Error(await apiErrorMessage(response, 'Failed to load dashboard data.'));
      if (!usersResponse.ok) throw new Error(await apiErrorMessage(usersResponse, 'Failed to load the user list.'));

      stats = await response.json();
      users = await usersResponse.json();
    } catch (err) {
      errorMsg = err instanceof Error ? err.message : 'Unknown error occurred.';
    } finally {
      isLoading = false;
    }
  });

  // The Export button used to do nothing. The accounts are already here, in
  // memory, so exporting them is a file the browser can write by itself - no
  // endpoint, no round trip. Only what the table shows: the API never sends a
  // password hash, and there is nothing else on an account to leak.
  function exportUsers() {
    const rows = [
      ['Id', 'Username', 'Role'],
      ...users.map((user) => [String(user.id), user.username, user.role])
    ];

    // Anything holding a quote, comma or newline has to be quoted, with inner
    // quotes doubled - a username is free text, so this is not hypothetical.
    const csv = rows
      .map((row) => row.map((cell) => `"${cell.replace(/"/g, '""')}"`).join(','))
      .join('\r\n');

    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
    const link = document.createElement('a');

    link.href = url;
    link.download = `inventria-users-${new Date().toISOString().slice(0, 10)}.csv`;
    link.click();

    URL.revokeObjectURL(url);
  }

  // Helper function to format dates elegantly
  function formatTimeAgo(dateString: string) {
    const date = new Date(dateString);
    return date.toLocaleString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  }
</script>

{#if allowed}
<Sidebar activePage="Dashboard" />
<Header />

<main class="dashboard-content">
  <div class="stats-grid">
    <!-- The bar under this figure was pinned to 100% - it was a full bar whatever
         the number above it said, because there is no capacity recorded anywhere
         to be a fraction of. A meter with no denominator is decoration that
         reads as information. -->
    <div class="stat-card">
      <h4>TOTAL STOCK (UNITS)</h4>
      <div class="value">{stats.totalStockQuantity.toLocaleString()}</div>
      <p class="subtext">Currently stored in warehouse</p>
    </div>
    <div class="stat-card">
      <h4>TOTAL USERS</h4>
      <div class="value">{stats.totalUsers}</div>
    </div>
    <div class="stat-card">
      <h4>MONTHLY THROUGHPUT</h4>
      <div class="value">{stats.monthlyThroughput.toLocaleString()}</div>
    </div>
    <div class="stat-card system-health">
      <h4>SYSTEM HEALTH</h4>
      <div class="value text-white">{errorMsg ? 'Error' : 'Stable'}</div>
      <p class="text-white">{errorMsg ? errorMsg : 'All nodes operational'}</p>
    </div>
  </div>

  <div class="middle-grid">
    <div class="panel">
      <h3>Inventory Categories</h3>
      <div class="chart-placeholder">
        <div class="donut">{stats.totalUniqueItems}<br/>SKUs</div>
        <div class="legend">
          {#if stats.distribution.length === 0}
            <p>No categories found.</p>
          {/if}
          {#each stats.distribution as item}
            <p>
              {item.category} 
              <strong>{Math.round((item.count / stats.totalUniqueItems) * 100)}%</strong>
            </p>
          {/each}
        </div>
      </div>
    </div>
    
    <div class="panel">
      <h3>System Activity</h3>
      <ul class="activity-list">
        {#if stats.recentActivity.length === 0}
          <li>No recent activity to display.</li>
        {/if}
        {#each stats.recentActivity as log}
          <li>
            <strong>{log.performedBy}</strong> 
            {#if log.transactionType === 'RECEIVE'}
               received {log.quantityChanged} units of {log.itemName}
            {:else if log.transactionType === 'PICK'}
               picked {Math.abs(log.quantityChanged)} units of {log.itemName}
            {:else if log.quantityChanged < 0}
               <!-- A relocation logs both of its sides, so the sign says which
                    half of the move this row is. -->
               relocated {Math.abs(log.quantityChanged)} units of {log.itemName} out of Bin {log.warehouseBinId}
            {:else}
               relocated {log.quantityChanged} units of {log.itemName} into Bin {log.warehouseBinId}
            {/if}
            <br/>
            <span>{formatTimeAgo(log.timestamp)}{log.transactionType === 'RELOCATE' ? '' : ` • Bin ${log.warehouseBinId}`}</span>
          </li>
        {/each}
      </ul>
      <!-- There is no full log screen to open: the API returns the five entries
           above and nothing else, so a working button would need a paged
           endpoint and a page to show it on. Marked the way the sidebar marks
           Reports and Settings rather than left looking clickable. -->
      <span class="unbuilt">View All Logs<span class="tag">SOON</span></span>
    </div>
  </div>

  <div class="panel mt-1">
    <div class="panel-header">
      <h3>User Management</h3>
      <!-- Add New User goes to the screen that creates one, rather than being a
           second, dead copy of it. -->
      <div class="actions">
        <button class="btn-outline" onclick={exportUsers} disabled={users.length === 0}>Export</button>
        <a class="btn-solid" href="/users">+ Add New User</a>
      </div>
    </div>
    <!-- Only the columns the API has answers for. Status and last-active went
         with the invented people: nothing records whether an account is on
         leave, and nothing records when it was last used, so anything in those
         columns would be the same fiction in a new costume. -->
    <table class="data-table">
      <thead>
        <tr>
          <th>ACCOUNT</th>
          <th>ROLE</th>
        </tr>
      </thead>
      <tbody>
        {#if isLoading}
          <tr><td colspan="2" class="empty-state">Loading users...</td></tr>
        {:else if users.length === 0}
          <tr><td colspan="2" class="empty-state">No users found.</td></tr>
        {:else}
          {#each users as user}
            <tr>
              <td><strong>{user.username}</strong><br/><span class="sub">#{user.id}</span></td>
              <td><span class="badge {user.role === 'Admin' ? 'admin' : 'employee'}">{user.role}</span></td>
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
  .stats-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 1.5rem; margin-bottom: 1.5rem; }
  .stat-card { background: white; padding: 1.5rem; border-radius: 8px; border: 1px solid #e2e8f0; }
  .stat-card h4 { margin: 0 0 1rem 0; font-size: 0.75rem; color: #64748b; letter-spacing: 0.5px; }
  .stat-card .value { font-size: 2rem; font-weight: 700; color: #0f172a; margin-bottom: 0.5rem; display: flex; align-items: center; gap: 0.5rem; }
  .stat-card .subtext { margin: 0; font-size: 0.8rem; color: #64748b; }
  .system-health { background: #0b6b36; border-color: #0b6b36; }
  .text-white { color: white !important; }
  .system-health h4 { color: #a7f3d0; }
  
  .middle-grid { display: grid; grid-template-columns: 2fr 1fr; gap: 1.5rem; }
  .panel { background: white; padding: 1.5rem; border-radius: 8px; border: 1px solid #e2e8f0; }
  .panel h3 { margin: 0 0 1.5rem 0; font-size: 1.1rem; color: #0f172a; }
  .chart-placeholder { display: flex; align-items: center; gap: 3rem; }
  .donut { width: 150px; height: 150px; border: 20px solid #0b6b36; border-radius: 20px; display: flex; align-items: center; justify-content: center; text-align: center; font-weight: bold; }
  
  .activity-list { list-style: none; padding: 0; margin: 0 0 1.5rem 0; }
  .activity-list li { margin-bottom: 1rem; font-size: 0.9rem; position: relative; padding-left: 1.5rem; }
  .activity-list li::before { content: ''; position: absolute; left: 0; top: 6px; width: 8px; height: 8px; border-radius: 50%; background: #cbd5e1; }
  .activity-list li:first-child::before { background: #0b6b36; }
  .activity-list li span { font-size: 0.75rem; color: #64748b; display: block; margin-top: 0.25rem; }
  
  .mt-1 { margin-top: 1.5rem; }
  .panel-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem; }
  .btn-outline { background: white; border: 1px solid #cbd5e1; padding: 0.5rem 1rem; border-radius: 6px; font-weight: 500; cursor: pointer; }
  .btn-outline:disabled { color: #94a3b8; cursor: not-allowed; }
  .btn-solid { background: #0b6b36; color: white; border: none; padding: 0.5rem 1rem; border-radius: 6px; font-weight: 500; cursor: pointer; margin-left: 0.5rem; text-decoration: none; display: inline-block; }
  .unbuilt { display: inline-flex; align-items: center; gap: 0.5rem; font-size: 0.9rem; font-weight: 500; color: #94a3b8; }
  .tag { font-size: 0.6rem; font-weight: 700; letter-spacing: 0.5px; color: #94a3b8; background: #f1f5f9; border: 1px solid #e2e8f0; border-radius: 4px; padding: 0.1rem 0.35rem; }
  
  .data-table { width: 100%; border-collapse: collapse; text-align: left; }
  .data-table th { padding: 1rem; border-bottom: 2px solid #e2e8f0; color: #64748b; font-size: 0.75rem; letter-spacing: 0.5px; }
  .data-table td { padding: 1rem; border-bottom: 1px solid #e2e8f0; font-size: 0.9rem; }
  .data-table td .sub { font-size: 0.8rem; color: #64748b; }
  .badge { padding: 0.25rem 0.75rem; border-radius: 20px; font-size: 0.75rem; font-weight: 600; }
  .badge.admin { background: #fee2e2; color: #991b1b; border: 1px solid #f87171; }
  .badge.employee { background: #e0f2fe; color: #0369a1; border: 1px solid #7dd3fc; }
  .empty-state { text-align: center; padding: 2rem; color: #64748b; font-style: italic; }
</style>