<script lang="ts">
  import InputField from '$lib/components/shared/InputField.svelte';
  import Button from '$lib/components/shared/Button.svelte';

  // State variables for the form inputs
  let itemId = $state('');
  let warehouseBinId = $state('');
  let quantity = $state('');
  
  // State variables for UI feedback
  let isLoading = $state(false);
  let message = $state('');
  let isError = $state(false);

  async function handleReceiveStock() {
    isLoading = true;
    message = '';
    isError = false;

    try {
      const token = localStorage.getItem('inventria_token');
      const currentUser = localStorage.getItem('inventria_user') || 'Unknown';

      const response = await fetch('http://localhost:5240/api/inventory/receive', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${token}`
        },
        body: JSON.stringify({
          itemId: parseInt(itemId),
          warehouseBinId: parseInt(warehouseBinId),
          quantity: parseInt(quantity),
          performedBy: currentUser
        })
      });

      const data = await response.json();

      if (!response.ok) {
        throw new Error(data.message || 'Failed to process transaction.');
      }

      // Show success message and clear the form
      message = data.message;
      itemId = '';
      warehouseBinId = '';
      quantity = '';

    } catch (err) {
      isError = true;
      message = err instanceof Error ? err.message : 'An network error occurred.';
    } finally {
      isLoading = false;
    }
  }
</script>

<div class="form-panel">
  <div class="panel-header">
    <h3>Inbound Receiving</h3>
    <p class="subtitle">Log new stock arrivals into the warehouse.</p>
  </div>

  <form onsubmit={handleReceiveStock} class="form-grid">
    <div class="input-row">
      <InputField 
        id="item-id" 
        label="ITEM ID" 
        placeholder="e.g., 1" 
        bind:value={itemId} 
        required={true} 
      />
      <InputField 
        id="bin-id" 
        label="WAREHOUSE BIN ID" 
        placeholder="e.g., 1" 
        bind:value={warehouseBinId} 
        required={true} 
      />
      <InputField 
        id="quantity" 
        type="number" 
        label="QUANTITY" 
        placeholder="Units received" 
        bind:value={quantity} 
        required={true} 
      />
    </div>

    {#if message}
      <div class="alert" class:alert-error={isError} class:alert-success={!isError}>
        {message}
      </div>
    {/if}

    <div class="submit-row">
      <Button type="submit" text="PROCESS ARRIVAL" {isLoading} />
    </div>
  </form>
</div>

<style>
  .form-panel { background: white; padding: 1.5rem; border-radius: 8px; border: 1px solid #e2e8f0; margin-bottom: 2rem; }
  .panel-header { margin-bottom: 1.5rem; }
  .panel-header h3 { margin: 0 0 0.25rem 0; font-size: 1.1rem; color: #0f172a; }
  .subtitle { margin: 0; font-size: 0.85rem; color: #64748b; }
  .input-row { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 1rem; }
  .submit-row { display: flex; justify-content: flex-end; margin-top: 1rem; width: 200px; margin-left: auto; }
  
  .alert { padding: 0.75rem; border-radius: 6px; font-size: 0.85rem; margin-top: 1rem; font-weight: 500; }
  .alert-error { background: #fee2e2; color: #991b1b; border: 1px solid #f87171; }
  .alert-success { background: #dcfce7; color: #166534; border: 1px solid #4ade80; }
</style>