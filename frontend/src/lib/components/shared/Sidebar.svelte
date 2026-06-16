<script lang="ts">
  export let activePage = "Dashboard";
</script>

<aside class="sidebar">
  <div class="brand">
    <div class="logo-icon">
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect><line x1="3" y1="9" x2="21" y2="9"></line><line x1="9" y1="21" x2="9" y2="9"></line></svg>
    </div>
    <div class="brand-text">
      <h2>Inventria</h2>
      <p>Warehouse v1.0</p>
    </div>
  </div>

  <nav class="nav-links">
    <a href="#" class:active={activePage === "Dashboard"}>Dashboard</a>
    <a href="#" class:active={activePage === "Inventory"}>Inventory</a>
    <a href="#" class:active={activePage === "Reports"}>Reports</a>
    <a href="#" class:active={activePage === "Users"}>Users</a>
    <a href="#" class:active={activePage === "Settings"}>Settings</a>
  </nav>

  <div class="sidebar-footer">
    <button class="btn-new">+ New Entry</button>
    <a href="/" class="logout">
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"></path><polyline points="16 17 21 12 16 7"></polyline><line x1="21" y1="12" x2="9" y2="12"></line></svg>
      Logout
    </a>
  </div>
</aside>

<style>
  .sidebar { width: 250px; height: 100vh; background: #fff; border-right: 1px solid #e2e8f0; display: flex; flex-direction: column; position: fixed; left: 0; top: 0; }
  .brand { padding: 1.5rem; display: flex; align-items: center; gap: 0.75rem; border-bottom: 1px solid #e2e8f0; }
  .logo-icon { width: 32px; height: 32px; background: #0b6b36; border-radius: 6px; display: flex; align-items: center; justify-content: center; }
  .brand-text h2 { margin: 0; font-size: 1.25rem; color: #0b6b36; }
  .brand-text p { margin: 0; font-size: 0.75rem; color: #64748b; }
  .nav-links { padding: 1.5rem 0; display: flex; flex-direction: column; flex-grow: 1; gap: 0.25rem; }
  .nav-links a { padding: 0.75rem 1.5rem; text-decoration: none; color: #475569; font-weight: 500; font-size: 0.9rem; border-left: 3px solid transparent; transition: all 0.2s; }
  .nav-links a:hover { background: #f8fafc; }
  .nav-links a.active { background: #eefdf4; color: #0b6b36; border-left-color: #0b6b36; }
  .sidebar-footer { padding: 1.5rem; display: flex; flex-direction: column; gap: 1rem; border-top: 1px solid #e2e8f0; }
  .btn-new { background: #0b6b36; color: white; border: none; padding: 0.75rem; border-radius: 6px; font-weight: 600; cursor: pointer; }
  .logout { display: flex; align-items: center; gap: 0.5rem; color: #475569; text-decoration: none; font-size: 0.9rem; font-weight: 500; }
</style>