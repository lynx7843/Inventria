<script lang="ts">
  type NavItem = {
    label: string;
    icon: string;
    active?: boolean;
  };

  const navItems: NavItem[] = [
    { label: 'Dashboard', icon: 'grid', active: true },
    { label: 'Inventory', icon: 'layers' },
    { label: 'Reports', icon: 'bar-chart' },
    { label: 'Users', icon: 'users' },
    { label: 'Settings', icon: 'settings' },
  ];

  let activeItem = 'Dashboard';
</script>

<aside class="sidebar">
  <div class="brand">
    <span class="brand-name">Inventria</span>
    <span class="brand-sub">Warehouse v1.0</span>
  </div>

  <nav class="nav">
    {#each navItems as item}
      <button
        class="nav-item"
        class:active={activeItem === item.label}
        on:click={() => (activeItem = item.label)}
      >
        <span class="nav-icon">
          {#if item.icon === 'grid'}
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/>
              <rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/>
            </svg>
          {:else if item.icon === 'layers'}
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polygon points="12 2 2 7 12 12 22 7 12 2"/>
              <polyline points="2 17 12 22 22 17"/>
              <polyline points="2 12 12 17 22 12"/>
            </svg>
          {:else if item.icon === 'bar-chart'}
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="20" x2="18" y2="10"/><line x1="12" y1="20" x2="12" y2="4"/>
              <line x1="6" y1="20" x2="6" y2="14"/><line x1="2" y1="20" x2="22" y2="20"/>
            </svg>
          {:else if item.icon === 'users'}
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
              <circle cx="9" cy="7" r="4"/>
              <path d="M23 21v-2a4 4 0 0 0-3-3.87"/>
              <path d="M16 3.13a4 4 0 0 1 0 7.75"/>
            </svg>
          {:else if item.icon === 'settings'}
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="3"/>
              <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/>
            </svg>
          {/if}
        </span>
        {item.label}
      </button>
    {/each}
  </nav>

  <div class="sidebar-bottom">
    <button class="new-entry-btn">
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
        <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
      </svg>
      New Entry
    </button>
    <button class="logout-btn">
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/>
        <polyline points="16 17 21 12 16 7"/>
        <line x1="21" y1="12" x2="9" y2="12"/>
      </svg>
      Logout
    </button>
  </div>
</aside>

<style>
  .sidebar {
    width: 224px;
    min-width: 224px;
    background: #fff;
    border-right: 1px solid #e5e7eb;
    display: flex;
    flex-direction: column;
    padding: 0;
    height: 100vh;
  }

  .brand {
    padding: 22px 22px 20px;
    border-bottom: 1px solid #f3f4f6;
  }

  .brand-name {
    display: block;
    font-size: 20px;
    font-weight: 800;
    color: #1a6b3c;
    letter-spacing: -0.5px;
  }

  .brand-sub {
    display: block;
    font-size: 11.5px;
    color: #9ca3af;
    margin-top: 1px;
    font-weight: 400;
  }

  .nav {
    flex: 1;
    padding: 12px 10px;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .nav-item {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 10px 12px;
    border-radius: 8px;
    border: none;
    background: transparent;
    color: #6b7280;
    font-size: 14px;
    font-weight: 500;
    cursor: pointer;
    width: 100%;
    text-align: left;
    transition: background 0.12s, color 0.12s;
  }

  .nav-item:hover {
    background: #f3f4f6;
    color: #111827;
  }

  .nav-item.active {
    background: #e8f5ee;
    color: #1a6b3c;
    font-weight: 600;
  }

  .nav-icon {
    display: flex;
    align-items: center;
    flex-shrink: 0;
  }

  .sidebar-bottom {
    padding: 16px 10px;
    border-top: 1px solid #f3f4f6;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }

  .new-entry-btn {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 8px;
    width: 100%;
    padding: 11px;
    background: #1a6b3c;
    color: #fff;
    border: none;
    border-radius: 8px;
    font-size: 14px;
    font-weight: 600;
    cursor: pointer;
    transition: background 0.15s;
  }

  .new-entry-btn:hover {
    background: #155c32;
  }

  .logout-btn {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 10px 12px;
    border-radius: 8px;
    border: none;
    background: transparent;
    color: #6b7280;
    font-size: 14px;
    font-weight: 500;
    cursor: pointer;
    width: 100%;
    transition: background 0.12s, color 0.12s;
  }

  .logout-btn:hover {
    background: #fef2f2;
    color: #ef4444;
  }
</style>
