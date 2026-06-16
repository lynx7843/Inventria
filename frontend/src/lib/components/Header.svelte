<script lang="ts">
  export let userName = "Admin User";
  export let role = "SYSTEM ROOT";
</script>

<header class="top-header">
  <div class="search-bar">
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#64748b" stroke-width="2"><circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line></svg>
    <input type="text" placeholder="Search inventory, users or logs..." />
  </div>
  <div class="user-actions">
    <button class="icon-btn">🔔</button>
    <button class="icon-btn">❓</button>
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
  .icon-btn { background: none; border: none; font-size: 1.2rem; cursor: pointer; color: #64748b; }
  .user-profile { display: flex; align-items: center; gap: 0.75rem; border-left: 1px solid #e2e8f0; padding-left: 1.5rem; }
  .user-profile .text { display: flex; flex-direction: column; text-align: right; }
  .name { font-weight: 600; font-size: 0.85rem; color: #0f172a; }
  .role { font-size: 0.7rem; color: #64748b; letter-spacing: 0.5px; }
  .avatar { width: 36px; height: 36px; background: #cbd5e1; border-radius: 50%; }
</style>