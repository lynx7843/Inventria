<script lang="ts">
  let { 
    text, 
    type = "button", 
    isLoading = false 
  } = $props();
</script>

<button {type} class="btn-solid" disabled={isLoading}>
  {isLoading ? 'AUTHENTICATING...' : text}
</button>

<style>
  .btn-solid { width: 100%; padding: 1rem; background: #0b6b36; color: white; border: none; border-radius: 6px; font-weight: 600; cursor: pointer; transition: background 0.2s; }
  .btn-solid:hover:not(:disabled) { background: #095028; }
  .btn-solid:disabled { background: #94a3b8; cursor: not-allowed; }
</style>