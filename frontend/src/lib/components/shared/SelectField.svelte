<script lang="ts">
  // A dropdown shaped like InputField, for the fields where the valid values are
  // rows in the database rather than anything a person should be typing.
  //
  // `options` is a list of { value, label }; `placeholder` is the disabled first
  // entry, and `emptyLabel` replaces it when there is nothing to choose from -
  // an empty dropdown otherwise looks broken rather than unpopulated.
  let {
    id,
    label,
    options = [],
    value = $bindable(),
    placeholder = 'Select...',
    emptyLabel = 'None available',
    required = false,
    disabled = false
  } = $props();
</script>

<div class="input-group">
  <label for={id}>{label}</label>
  <select {id} bind:value {required} disabled={disabled || options.length === 0}>
    <option value="" disabled selected>
      {options.length === 0 ? emptyLabel : placeholder}
    </option>
    {#each options as option (option.value)}
      <option value={String(option.value)}>{option.label}</option>
    {/each}
  </select>
</div>

<style>
  .input-group { margin-bottom: 1.5rem; }
  label { display: block; font-size: 0.75rem; font-weight: 600; color: #475569; margin-bottom: 0.5rem; letter-spacing: 0.5px; }
  select { width: 100%; padding: 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px; box-sizing: border-box; outline: none; background: white; font-family: inherit; transition: border 0.2s; }
  select:focus { border-color: #0b6b36; }
  select:disabled { background: #f1f5f9; color: #94a3b8; cursor: not-allowed; }
</style>
