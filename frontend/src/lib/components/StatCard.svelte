<script lang="ts">
  export let label: string;
  export let value: string;
  export let badge: string;
  export let badgeType: 'positive' | 'critical' | 'active';
  export let icon: string;
</script>

<div class="stat-card">
  <div class="card-top">
    <div class="icon-wrap" class:icon-positive={badgeType === 'positive'} class:icon-critical={badgeType === 'critical'} class:icon-active={badgeType === 'active'}>
      {#if icon === 'archive'}
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polyline points="21 8 21 21 3 21 3 8"/>
          <rect x="1" y="3" width="22" height="5"/>
          <line x1="10" y1="12" x2="14" y2="12"/>
        </svg>
      {:else if icon === 'warning'}
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/>
          <line x1="12" y1="9" x2="12" y2="13"/>
          <line x1="12" y1="17" x2="12.01" y2="17"/>
        </svg>
      {:else if icon === 'check'}
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polyline points="9 11 12 14 22 4"/>
          <path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"/>
        </svg>
      {/if}
    </div>
    <span class="badge" class:badge-positive={badgeType === 'positive'} class:badge-critical={badgeType === 'critical'} class:badge-active={badgeType === 'active'}>
      {badge}
    </span>
  </div>
  <p class="card-label">{label}</p>
  <p class="card-value">{value}</p>
</div>

<style>
  .stat-card {
    background: #fff;
    border: 1px solid #e5e7eb;
    border-radius: 12px;
    padding: 20px 24px;
    display: flex;
    flex-direction: column;
    gap: 6px;
  }

  .card-top {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: 8px;
  }

  .icon-wrap {
    width: 40px;
    height: 40px;
    border-radius: 10px;
    display: flex;
    align-items: center;
    justify-content: center;
  }

  .icon-positive {
    background: #e8f5ee;
    color: #1a6b3c;
  }

  .icon-critical {
    background: #fff1f1;
    color: #ef4444;
  }

  .icon-active {
    background: #e8f5ee;
    color: #1a6b3c;
  }

  .badge {
    font-size: 12px;
    font-weight: 600;
    padding: 3px 8px;
    border-radius: 6px;
  }

  .badge-positive {
    background: #e8f5ee;
    color: #1a6b3c;
  }

  .badge-critical {
    color: #ef4444;
    background: transparent;
    padding-right: 0;
  }

  .badge-active {
    color: #6b7280;
    background: transparent;
    padding-right: 0;
  }

  .card-label {
    font-size: 11px;
    font-weight: 600;
    letter-spacing: 0.06em;
    color: #9ca3af;
    margin: 0;
    text-transform: uppercase;
  }

  .card-value {
    font-size: 32px;
    font-weight: 700;
    color: #111827;
    margin: 0;
    letter-spacing: -1px;
    line-height: 1.1;
  }
</style>
