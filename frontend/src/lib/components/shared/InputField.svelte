<script lang="ts">
  // Using Svelte 5 $bindable() so the parent can read what the user types
  let { 
    id, 
    label, 
    type = "text", 
    placeholder = "", 
    value = $bindable(), 
    required = false 
  } = $props();
</script>

<div class="input-group">
  <label for={id}>{label}</label>
  <input {type} {id} bind:value {placeholder} {required} />
</div>

<style>
  .input-group { margin-bottom: 1.5rem; }
  label { display: block; font-size: 0.75rem; font-weight: 600; color: #475569; margin-bottom: 0.5rem; letter-spacing: 0.5px; }
  input { width: 100%; padding: 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px; box-sizing: border-box; outline: none; transition: border 0.2s; }
  input:focus { border-color: #0b6b36; }
</style>