<script lang="ts">
  import { goto } from '$app/navigation';
  import { apiFetch } from '$lib/api';
  import { clearSession, getRole, homeFor } from '$lib/auth';

  // Which nav entry to mark as current. A string rather than a union of the
  // page names: the entries are data here, and a page that misspells its own
  // name simply highlights nothing.
  let {
    activePage = "Dashboard"
  }: {
    activePage?: string;
  } = $props();

  // Employees and Admins have different dashboards, and only an Admin has a
  // Users page to reach - the guard on it would bounce anyone else straight
  // back, so don't offer the trip.
  const role = getRole();
  const dashboardHref = role ? homeFor(role) : '/';
  const isAdmin = role === 'Admin';

  // The session cookie is HttpOnly, so only the server can clear it - navigating
  // away would otherwise leave a valid session behind for the rest of its life.
  async function handleLogout() {
    try {
      await apiFetch('/api/auth/logout', { method: 'POST' });
    } catch (err) {
      // A failed call still shouldn't strand the user on a signed-in screen.
      console.error('Logout request failed', err);
    } finally {
      clearSession();
      goto('/');
    }
  }
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

  <!-- Every entry that looks like a link is one. Reports and Settings have no
       page behind them yet, and an href="#" that quietly does nothing reads as a
       broken app rather than an unfinished one - so they say so instead, and
       cannot be clicked or tabbed into until there is somewhere to go. -->
  <nav class="nav-links">
    <a href={dashboardHref} class:active={activePage === "Dashboard"}>Dashboard</a>
    <a href="/inventory" class:active={activePage === "Inventory"}>Inventory</a>
    <a href="/bins" class:active={activePage === "Bins"}>Bins</a>
    <span class="nav-item unbuilt" aria-disabled="true">Reports<span class="tag">SOON</span></span>
    {#if isAdmin}
      <a href="/users" class:active={activePage === "Users"}>Users</a>
    {/if}
    <span class="nav-item unbuilt" aria-disabled="true">Settings<span class="tag">SOON</span></span>
  </nav>

  <div class="sidebar-footer">
    <!-- The one button in the sidebar that did nothing at all. A "new entry" in
         a warehouse system is a new product definition, so it opens that form
         directly rather than dropping you on the page to hunt for it. -->
    <a class="btn-new" href="/inventory?new=1">+ New Entry</a>
    <button type="button" class="logout" onclick={handleLogout}>
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"></path><polyline points="16 17 21 12 16 7"></polyline><line x1="21" y1="12" x2="9" y2="12"></line></svg>
      Logout
    </button>
  </div>
</aside>

<style>
  .sidebar { width: 250px; height: 100vh; background: #fff; border-right: 1px solid #e2e8f0; display: flex; flex-direction: column; position: fixed; left: 0; top: 0; }
  .brand { padding: 1.5rem; display: flex; align-items: center; gap: 0.75rem; border-bottom: 1px solid #e2e8f0; }
  .logo-icon { width: 32px; height: 32px; background: #0b6b36; border-radius: 6px; display: flex; align-items: center; justify-content: center; }
  .brand-text h2 { margin: 0; font-size: 1.25rem; color: #0b6b36; }
  .brand-text p { margin: 0; font-size: 0.75rem; color: #64748b; }
  .nav-links { padding: 1.5rem 0; display: flex; flex-direction: column; flex-grow: 1; gap: 0.25rem; }
  .nav-links a, .nav-links .nav-item { padding: 0.75rem 1.5rem; text-decoration: none; color: #475569; font-weight: 500; font-size: 0.9rem; border-left: 3px solid transparent; transition: all 0.2s; }
  .nav-links a:hover { background: #f8fafc; }
  .nav-links a.active { background: #eefdf4; color: #0b6b36; border-left-color: #0b6b36; }
  .nav-links .unbuilt { display: flex; align-items: center; justify-content: space-between; color: #94a3b8; cursor: default; }
  .nav-links .tag { font-size: 0.6rem; font-weight: 700; letter-spacing: 0.5px; color: #94a3b8; background: #f1f5f9; border: 1px solid #e2e8f0; border-radius: 4px; padding: 0.1rem 0.35rem; }
  .sidebar-footer { padding: 1.5rem; display: flex; flex-direction: column; gap: 1rem; border-top: 1px solid #e2e8f0; }
  .btn-new { background: #0b6b36; color: white; border: none; padding: 0.75rem; border-radius: 6px; font-weight: 600; cursor: pointer; text-align: center; text-decoration: none; font-size: 0.9rem; }
  .btn-new:hover { background: #095028; }
  .logout { display: flex; align-items: center; gap: 0.5rem; color: #475569; text-decoration: none; font-size: 0.9rem; font-weight: 500; background: none; border: none; padding: 0; font-family: inherit; cursor: pointer; }
</style>