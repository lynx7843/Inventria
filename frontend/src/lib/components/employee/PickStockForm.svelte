<script lang="ts">
  import InputField from '$lib/components/shared/InputField.svelte';
  import Button from '$lib/components/shared/Button.svelte';

  let itemId = $state('');
  let warehouseBinId = $state('');
  let quantity = $state('');
  
  let isLoading = $state(false);
  let message = $state('');
  let isError = $state(false);

  async function handlePickStock() {
    isLoading = true;
    message = '';
    isError = false;

    try {
      const response = await fetch('http://localhost:5240/api/inventory/pick', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          itemId: parseInt(itemId),
          warehouseBinId: parseInt(warehouseBinId),
          quantity: parseInt(quantity),
          performedBy: "Alice Smith" 
        })
      });

      const data = await response.json();

      if (!response.ok) {
        throw new Error(data.message || 'Failed to process transaction.');
      }

      message = data.message;
      itemId = '';
      warehouseBinId = '';
      quantity = '';
    } catch (err) {
      isError = true;
      message = err instanceof Error ? err.message : 'A network error occurred.';
    } finally {
      isLoading = false;
    }
  }
</script>

<div class="form-panel">
  <div class="panel-header">
    <h3>Order Fulfillment (Pick)</h3>
    <p class="subtitle">Deduct items from a specific bin for outbound shipping.</p>
  </div>

  <form onsubmit={handlePickStock} class="form-grid">
    <div class="input-row">
      <InputField id="pick-item-id" label="ITEM ID" placeholder="e.g., 1" bind:value={itemId} required={true} />
      <InputField id="pick-bin-id" label="SOURCE BIN ID" placeholder="e.g., 1" bind:value={warehouseBinId} required={true} />
      <InputField id="pick-quantity" type="number" label="QUANTITY" placeholder="Units to pick" bind:value={quantity} required={true} />
    </div>

    {#if message}
      <div class="alert" class:alert-error={isError} class:alert-success={!isError}>
        {message}
      </div>
    {/if}

    <div class="submit-row">
      <Button type="submit" text="PROCESS PICK" {isLoading} />
    </div>
  </form>
</div>

<style>
  .form-panel { background: white; padding: 1.5rem; border-radius: 8px; border: 1px solid #e2e8f0; margin-bottom: 2rem; }
  .panel-header { margin-bottom: 1.5rem; }
  .panel-header h3 { margin: 0 0 0.25rem 0; font-size: 1.1rem; color: #0f172a; }
  .subtitle { margin: 0; font-size: 0.85rem; color: #64748b; }
  .input-row { display: grid; grid-template-columns: repeat(3, 1fr); gap: 1rem; }
  .submit-row { display: flex; justify-content: flex-end; margin-top: 1rem; width: 200px; margin-left: auto; }
  
  .alert { padding: 0.75rem; border-radius: 6px; font-size: 0.85rem; margin-top: 1rem; font-weight: 500; }
  .alert-error { background: #fee2e2; color: #991b1b; border: 1px solid #f87171; }
  .alert-success { background: #dcfce7; color: #166534; border: 1px solid #4ade80; }
</style>