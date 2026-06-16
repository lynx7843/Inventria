<script lang="ts">
  import Sidebar from './Sidebar.svelte';
  import TopBar from './TopBar.svelte';
  import StatCard from './StatCard.svelte';
  import InventoryTable from './InventoryTable.svelte';

  interface InventoryItem {
    id: number;
    name: string;
    sku: string;
    category: string;
    quantity: number;
    status: 'IN-STOCK' | 'LOW STOCK' | 'OUT OF STOCK';
    icon: string;
  }

  const stats = [
    {
      label: 'TOTAL ITEMS',
      value: '12,482',
      badge: '+2.5%',
      badgeType: 'positive' as const,
      icon: 'archive',
    },
    {
      label: 'LOW STOCK ALERTS',
      value: '14',
      badge: 'Critical',
      badgeType: 'critical' as const,
      icon: 'warning',
    },
    {
      label: 'ITEMS RECEIVED TODAY',
      value: '342',
      badge: 'Active',
      badgeType: 'active' as const,
      icon: 'check',
    },
  ];

  const items: InventoryItem[] = [
    {
      id: 1,
      name: 'Industrial Fan Alpha',
      sku: 'SKU-20445-IF',
      category: 'Electronics',
      quantity: 142,
      status: 'IN-STOCK',
      icon: 'box',
    },
    {
      id: 2,
      name: 'Heavy Duty Cable (50m)',
      sku: 'SKU-99210-CB',
      category: 'Electrical',
      quantity: 12,
      status: 'LOW STOCK',
      icon: 'bolt',
    },
    {
      id: 3,
      name: 'Steel Wrench Set',
      sku: 'SKU-31120-TW',
      category: 'Tools',
      quantity: 58,
      status: 'IN-STOCK',
      icon: 'tool',
    },
    {
      id: 4,
      name: 'Pallet Wrap Stretch Film',
      sku: 'SKU-88211-SF',
      category: 'Packaging',
      quantity: 0,
      status: 'OUT OF STOCK',
      icon: 'clipboard',
    },
    {
      id: 5,
      name: 'Safety Goggles (Anti-Fog)',
      sku: 'SKU-44001-SG',
      category: 'Safety',
      quantity: 210,
      status: 'IN-STOCK',
      icon: 'eye',
    },
  ];

  let currentPage = 1;
  const totalItems = 12482;
</script>

<div class="app-layout">
  <Sidebar />
  <div class="main-content">
    <TopBar />
    <div class="page-body">
      <!-- Header -->
      <div class="page-header">
        <div>
          <h1 class="page-title">Employee Dashboard</h1>
          <p class="page-subtitle">Welcome back. Here's what's happening in the warehouse today.</p>
        </div>
        <div class="header-actions">
          <button class="btn-outline">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <rect x="3" y="3" width="7" height="7" rx="1"/>
              <rect x="14" y="3" width="7" height="7" rx="1"/>
              <rect x="3" y="14" width="7" height="7" rx="1"/>
              <rect x="14" y="14" width="7" height="7" rx="1"/>
            </svg>
            Scan Item
          </button>
          <button class="btn-primary">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
              <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
            </svg>
            Update Stock
          </button>
        </div>
      </div>

      <!-- Stat Cards -->
      <div class="stats-grid">
        {#each stats as stat}
          <StatCard {...stat} />
        {/each}
        <!-- Efficiency card -->
        <div class="stat-card efficiency-card">
          <span class="efficiency-label">EFFICIENCY RATE</span>
          <span class="efficiency-value">98.4%</span>
          <div class="efficiency-bar">
            <div class="efficiency-fill" style="width: 98.4%"></div>
          </div>
        </div>
      </div>

      <!-- Inventory Table -->
      <InventoryTable {items} {totalItems} bind:currentPage />

      <!-- Footer -->
      <div class="page-footer">
        <div class="status-dot"></div>
        <span class="status-text">System Status: All nodes operational. Last sync 2m ago.</span>
        <div class="footer-links">
          <a href="#privacy">Privacy Policy</a>
          <a href="#terms">Terms of Service</a>
          <a href="#support">Support</a>
        </div>
      </div>
    </div>
  </div>
</div>

<style>
  .app-layout {
    display: flex;
    height: 100vh;
    overflow: hidden;
    background: #f8faf8;
    font-family: 'Inter', -apple-system, BlinkMacSystemFont, sans-serif;
  }

  .main-content {
    flex: 1;
    display: flex;
    flex-direction: column;
    overflow: hidden;
  }

  .page-body {
    flex: 1;
    overflow-y: auto;
    padding: 28px 36px;
    display: flex;
    flex-direction: column;
    gap: 24px;
  }

  .page-header {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
  }

  .page-title {
    font-size: 24px;
    font-weight: 700;
    color: #111827;
    margin: 0 0 4px;
    letter-spacing: -0.3px;
  }

  .page-subtitle {
    font-size: 14px;
    color: #6b7280;
    margin: 0;
  }

  .header-actions {
    display: flex;
    gap: 10px;
    align-items: center;
  }

  .btn-outline {
    display: flex;
    align-items: center;
    gap: 7px;
    padding: 9px 16px;
    border: 1.5px solid #d1d5db;
    border-radius: 8px;
    background: #fff;
    color: #374151;
    font-size: 13.5px;
    font-weight: 500;
    cursor: pointer;
    transition: border-color 0.15s, background 0.15s;
  }

  .btn-outline:hover {
    border-color: #9ca3af;
    background: #f9fafb;
  }

  .btn-primary {
    display: flex;
    align-items: center;
    gap: 7px;
    padding: 9px 16px;
    border: none;
    border-radius: 8px;
    background: #1a6b3c;
    color: #fff;
    font-size: 13.5px;
    font-weight: 500;
    cursor: pointer;
    transition: background 0.15s;
  }

  .btn-primary:hover {
    background: #155c32;
  }

  .stats-grid {
    display: grid;
    grid-template-columns: 1fr 1fr 1fr 1.1fr;
    gap: 16px;
  }

  .efficiency-card {
    background: #1a6b3c;
    border-radius: 12px;
    padding: 20px 24px;
    display: flex;
    flex-direction: column;
    gap: 8px;
    color: #fff;
  }

  .efficiency-label {
    font-size: 11px;
    font-weight: 600;
    letter-spacing: 0.06em;
    color: rgba(255,255,255,0.75);
  }

  .efficiency-value {
    font-size: 36px;
    font-weight: 700;
    color: #fff;
    letter-spacing: -1px;
    line-height: 1;
  }

  .efficiency-bar {
    margin-top: 4px;
    height: 6px;
    background: rgba(255,255,255,0.25);
    border-radius: 99px;
    overflow: hidden;
  }

  .efficiency-fill {
    height: 100%;
    background: #fff;
    border-radius: 99px;
  }

  .page-footer {
    display: flex;
    align-items: center;
    gap: 8px;
    padding-top: 8px;
    margin-top: auto;
  }

  .status-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: #22c55e;
    flex-shrink: 0;
  }

  .status-text {
    font-size: 13px;
    color: #6b7280;
    flex: 1;
  }

  .footer-links {
    display: flex;
    gap: 20px;
  }

  .footer-links a {
    font-size: 13px;
    color: #6b7280;
    text-decoration: none;
    transition: color 0.15s;
  }

  .footer-links a:hover {
    color: #374151;
  }
</style>
