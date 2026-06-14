<script lang="ts">
  import Dashboard from './lib/components/Dashboard.svelte';
</script>

<Dashboard />

<style>
  :global(*, *::before, *::after) {
    box-sizing: border-box;
  }

  :global(body) {
    margin: 0;
    padding: 0;
    font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
    background: #f8faf8;
    color: #374151;
    -webkit-font-smoothing: antialiased;
  }

  :global(button) {
    font-family: inherit;
  }

  :global(input) {
    font-family: inherit;
  }
</style>
