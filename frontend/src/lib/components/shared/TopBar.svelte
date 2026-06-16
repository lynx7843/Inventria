<script lang="ts">
  let searchValue = '';
</script>

<header class="topbar">
  <div class="search-wrap">
    <svg class="search-icon" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
      <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
    </svg>
    <input
      class="search-input"
      type="text"
      placeholder="Search inventory items, SKUs, or locations..."
      bind:value={searchValue}
    />
  </div>

  <div class="topbar-right">
    <button class="icon-btn" aria-label="Notifications">
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/>
        <path d="M13.73 21a2 2 0 0 1-3.46 0"/>
      </svg>
    </button>
    <button class="icon-btn" aria-label="Help">
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <circle cx="12" cy="12" r="10"/>
        <path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3"/>
        <line x1="12" y1="17" x2="12.01" y2="17"/>
      </svg>
    </button>
    <button class="avatar-btn" aria-label="Profile">
      <img
        src="https://i.pravatar.cc/36?img=12"
        alt="User avatar"
        class="avatar"
      />
    </button>
  </div>
</header>

<style>
  .topbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 12px 36px;
    background: #fff;
    border-bottom: 1px solid #e5e7eb;
    gap: 16px;
    min-height: 60px;
    flex-shrink: 0;
  }

  .search-wrap {
    position: relative;
    flex: 1;
    max-width: 520px;
  }

  .search-icon {
    position: absolute;
    left: 13px;
    top: 50%;
    transform: translateY(-50%);
    color: #9ca3af;
    pointer-events: none;
  }

  .search-input {
    width: 100%;
    padding: 9px 14px 9px 38px;
    border: 1.5px solid #e5e7eb;
    border-radius: 8px;
    font-size: 13.5px;
    color: #374151;
    background: #f9fafb;
    outline: none;
    transition: border-color 0.15s, background 0.15s;
    box-sizing: border-box;
  }

  .search-input::placeholder {
    color: #9ca3af;
  }

  .search-input:focus {
    border-color: #1a6b3c;
    background: #fff;
  }

  .topbar-right {
    display: flex;
    align-items: center;
    gap: 4px;
  }

  .icon-btn {
    width: 36px;
    height: 36px;
    border-radius: 8px;
    border: none;
    background: transparent;
    color: #6b7280;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    transition: background 0.12s, color 0.12s;
  }

  .icon-btn:hover {
    background: #f3f4f6;
    color: #374151;
  }

  .avatar-btn {
    width: 36px;
    height: 36px;
    border-radius: 50%;
    border: 2px solid #e5e7eb;
    background: transparent;
    padding: 0;
    cursor: pointer;
    overflow: hidden;
    transition: border-color 0.15s;
    margin-left: 4px;
  }

  .avatar-btn:hover {
    border-color: #1a6b3c;
  }

  .avatar {
    width: 100%;
    height: 100%;
    object-fit: cover;
    display: block;
  }
</style>
