<script lang="ts">
  import { getUsername, getRole } from '$lib/auth';

  // Whoever is actually signed in. Every page used to hand this component a name
  // typed into the markup - "Admin User" on four of them, "Alice Smith" on the
  // employee dashboard - so the header greeted the same fictional person no
  // matter who logged in. The session already knows; ask it.
  let {
    userName = getUsername() ?? 'Signed in',
    role = getRole() ?? ''
  }: {
    userName?: string;
    role?: string;
  } = $props();
</script>

<header class="top-header">
  <div class="search-bar">
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#64748b" stroke-width="2"><circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line></svg>
    <input type="text" placeholder="Search inventory, users or logs..." />
  </div>
  <!-- A bell with no notifications behind it and a help button with no help:
       both were decoration that invited a click and swallowed it. -->
  <div class="user-actions">
    <div class="user-profile">
      <div class="text">
        <span class="name">{userName}</span>
        <span class="role">{role}</span>
      </div>
      <div class="avatar"></div>
    </div>
  </div>
</header>

<style>
  .top-header { height: 70px; background: #fff; border-bottom: 1px solid #e2e8f0; display: flex; align-items: center; justify-content: space-between; padding: 0 2rem; margin-left: 250px; }
  .search-bar { display: flex; align-items: center; background: #f1f5f9; padding: 0.5rem 1rem; border-radius: 20px; width: 400px; gap: 0.5rem; }
  .search-bar input { border: none; background: transparent; outline: none; width: 100%; font-size: 0.9rem; color: #334155; }
  .user-actions { display: flex; align-items: center; gap: 1.5rem; }
  .user-profile { display: flex; align-items: center; gap: 0.75rem; }
  .user-profile .text { display: flex; flex-direction: column; text-align: right; }
  .name { font-weight: 600; font-size: 0.85rem; color: #0f172a; }
  .role { font-size: 0.7rem; color: #64748b; letter-spacing: 0.5px; }
  .avatar { width: 36px; height: 36px; background: #cbd5e1; border-radius: 50%; }
</style>