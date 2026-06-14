<script lang="ts">
  interface InventoryItem {
    id: number;
    name: string;
    sku: string;
    category: string;
    quantity: number;
    status: 'IN-STOCK' | 'LOW STOCK' | 'OUT OF STOCK';
    icon: string;
  }

  export let items: InventoryItem[];
  export let totalItems: number;
  export let currentPage: number = 1;

  const totalPages = 3; // simplified for display

  function goToPage(page: number) {
    if (page >= 1 && page <= totalPages) {
      currentPage = page;
    }
  }

  let openMenuId: number | null = null;

  function toggleMenu(id: number) {
    openMenuId = openMenuId === id ? null : id;
  }

  function getStatusClass(status: string): string {
    if (status === 'IN-STOCK') return 'status-in-stock';
    if (status === 'LOW STOCK') return 'status-low-stock';
    return 'status-out-of-stock';
  }
</script>

<div class="table-card">
  <div class="table-header">
    <h2 class="table-title">Inventory Overview</h2>
    <div class="table-actions">
      <button class="icon-btn" aria-label="Filter">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <line x1="4" y1="6" x2="20" y2="6"/><line x1="8" y1="12" x2="16" y2="12"/><line x1="11" y1="18" x2="13" y2="18"/>
        </svg>
      </button>
      <button class="icon-btn" aria-label="Download">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
          <polyline points="7 10 12 15 17 10"/>
          <line x1="12" y1="15" x2="12" y2="3"/>
        </svg>
      </button>
    </div>
  </div>

  <table class="table">
    <thead>
      <tr>
        <th>ITEM NAME</th>
        <th>SKU</th>
        <th>CATEGORY</th>
        <th>QUANTITY</th>
        <th>STATUS</th>
        <th>ACTIONS</th>
      </tr>
    </thead>
    <tbody>
      {#each items as item (item.id)}
        <tr>
          <td>
            <div class="item-name-cell">
              <div class="item-icon">
                {#if item.icon === 'box'}
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#1a6b3c" stroke-width="1.8">
                    <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/>
                    <polyline points="3.27 6.96 12 12.01 20.73 6.96"/>
                    <line x1="12" y1="22.08" x2="12" y2="12"/>
                  </svg>
                {:else if item.icon === 'bolt'}
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#1a6b3c" stroke-width="1.8">
                    <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/>
                  </svg>
                {:else if item.icon === 'tool'}
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#1a6b3c" stroke-width="1.8">
                    <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
                  </svg>
                {:else if item.icon === 'clipboard'}
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#1a6b3c" stroke-width="1.8">
                    <path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"/>
                    <rect x="8" y="2" width="8" height="4" rx="1" ry="1"/>
                  </svg>
                {:else if item.icon === 'eye'}
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#1a6b3c" stroke-width="1.8">
                    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/>
                    <circle cx="12" cy="12" r="3"/>
                  </svg>
                {/if}
              </div>
              <span class="item-name">{item.name}</span>
            </div>
          </td>
          <td class="sku-cell">{item.sku}</td>
          <td>{item.category}</td>
          <td>{item.quantity} units</td>
          <td>
            <span class="status-badge {getStatusClass(item.status)}">
              {item.status}
            </span>
          </td>
          <td>
            <div class="menu-wrap">
              <button class="menu-btn" on:click={() => toggleMenu(item.id)} aria-label="More actions">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="5" r="1" fill="currentColor"/>
                  <circle cx="12" cy="12" r="1" fill="currentColor"/>
                  <circle cx="12" cy="19" r="1" fill="currentColor"/>
                </svg>
              </button>
              {#if openMenuId === item.id}
                <div class="dropdown-menu">
                  <button on:click={() => (openMenuId = null)}>Edit</button>
                  <button on:click={() => (openMenuId = null)}>View Details</button>
                  <button class="danger" on:click={() => (openMenuId = null)}>Delete</button>
                </div>
              {/if}
            </div>
          </td>
        </tr>
      {/each}
    </tbody>
  </table>

  <div class="table-footer">
    <span class="showing-text">Showing {items.length} of {totalItems.toLocaleString()} items</span>
    <div class="pagination">
      <button
        class="page-btn"
        disabled={currentPage === 1}
        on:click={() => goToPage(currentPage - 1)}
      >Previous</button>
      {#each [1, 2, 3] as page}
        <button
          class="page-btn page-num"
          class:active={currentPage === page}
          on:click={() => goToPage(page)}
        >{page}</button>
      {/each}
      <button
        class="page-btn"
        disabled={currentPage === totalPages}
        on:click={() => goToPage(currentPage + 1)}
      >Next</button>
    </div>
  </div>
</div>

<style>
  .table-card {
    background: #fff;
    border: 1px solid #e5e7eb;
    border-radius: 12px;
    overflow: hidden;
  }

  .table-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 20px 24px;
    border-bottom: 1px solid #f3f4f6;
  }

  .table-title {
    font-size: 16px;
    font-weight: 700;
    color: #111827;
    margin: 0;
  }

  .table-actions {
    display: flex;
    gap: 6px;
  }

  .icon-btn {
    width: 34px;
    height: 34px;
    border-radius: 7px;
    border: 1.5px solid #e5e7eb;
    background: #fff;
    color: #6b7280;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    transition: border-color 0.12s, color 0.12s;
  }

  .icon-btn:hover {
    border-color: #9ca3af;
    color: #374151;
  }

  .table {
    width: 100%;
    border-collapse: collapse;
  }

  thead tr {
    background: #f9fafb;
    border-bottom: 1px solid #e5e7eb;
  }

  th {
    padding: 11px 24px;
    text-align: left;
    font-size: 11px;
    font-weight: 600;
    letter-spacing: 0.06em;
    color: #9ca3af;
    white-space: nowrap;
  }

  tbody tr {
    border-bottom: 1px solid #f3f4f6;
    transition: background 0.1s;
  }

  tbody tr:last-child {
    border-bottom: none;
  }

  tbody tr:hover {
    background: #fafafa;
  }

  td {
    padding: 16px 24px;
    font-size: 14px;
    color: #374151;
  }

  .item-name-cell {
    display: flex;
    align-items: center;
    gap: 12px;
  }

  .item-icon {
    width: 36px;
    height: 36px;
    border-radius: 8px;
    background: #e8f5ee;
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
  }

  .item-name {
    font-weight: 500;
    color: #111827;
  }

  .sku-cell {
    color: #6b7280;
    font-size: 13px;
    font-family: 'SF Mono', 'Fira Code', monospace;
  }

  .status-badge {
    display: inline-block;
    padding: 4px 10px;
    border-radius: 6px;
    font-size: 12px;
    font-weight: 600;
    letter-spacing: 0.04em;
    white-space: nowrap;
  }

  .status-in-stock {
    background: #e8f5ee;
    color: #1a6b3c;
  }

  .status-low-stock {
    background: #fff1f0;
    color: #e53e3e;
  }

  .status-out-of-stock {
    background: #f3f4f6;
    color: #6b7280;
  }

  .menu-wrap {
    position: relative;
    display: inline-block;
  }

  .menu-btn {
    width: 32px;
    height: 32px;
    border-radius: 6px;
    border: none;
    background: transparent;
    color: #9ca3af;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    transition: background 0.12s, color 0.12s;
  }

  .menu-btn:hover {
    background: #f3f4f6;
    color: #374151;
  }

  .dropdown-menu {
    position: absolute;
    right: 0;
    top: 36px;
    background: #fff;
    border: 1px solid #e5e7eb;
    border-radius: 8px;
    box-shadow: 0 4px 16px rgba(0,0,0,0.1);
    z-index: 10;
    min-width: 140px;
    overflow: hidden;
  }

  .dropdown-menu button {
    display: block;
    width: 100%;
    padding: 9px 14px;
    border: none;
    background: transparent;
    text-align: left;
    font-size: 13.5px;
    color: #374151;
    cursor: pointer;
    transition: background 0.1s;
  }

  .dropdown-menu button:hover {
    background: #f3f4f6;
  }

  .dropdown-menu button.danger {
    color: #ef4444;
  }

  .dropdown-menu button.danger:hover {
    background: #fff1f1;
  }

  .table-footer {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 14px 24px;
    border-top: 1px solid #e5e7eb;
    background: #fafafa;
  }

  .showing-text {
    font-size: 13px;
    color: #6b7280;
  }

  .pagination {
    display: flex;
    gap: 4px;
    align-items: center;
  }

  .page-btn {
    padding: 6px 12px;
    border: 1.5px solid #e5e7eb;
    border-radius: 7px;
    background: #fff;
    font-size: 13px;
    color: #374151;
    cursor: pointer;
    font-weight: 500;
    transition: border-color 0.12s, background 0.12s;
  }

  .page-btn:hover:not(:disabled) {
    border-color: #9ca3af;
    background: #f9fafb;
  }

  .page-btn:disabled {
    opacity: 0.45;
    cursor: not-allowed;
  }

  .page-num.active {
    background: #1a6b3c;
    border-color: #1a6b3c;
    color: #fff;
  }
</style>
