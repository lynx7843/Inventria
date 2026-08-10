<script lang="ts">
  import InputField from '$lib/components/shared/InputField.svelte';
  import SelectField from '$lib/components/shared/SelectField.svelte';
  import Button from '$lib/components/shared/Button.svelte';
  import { onMount } from 'svelte';
  import { apiFetch } from '$lib/api';
  import { fetchBins, fetchItems, binLabel, itemLabel, type Option } from '$lib/inventory';

  let itemId = $state('');
  let warehouseBinId = $state('');
  let quantity = $state('');

  let itemOptions: Option[] = $state([]);
  let binOptions: Option[] = $state([]);
  let isLoadingOptions = $state(true);

  let isLoading = $state(false);
  let message = $state('');
  let isError = $state(false);

  onMount(async () => {
    try {
      const [items, bins] = await Promise.all([fetchItems(), fetchBins()]);
      itemOptions = items.map((item) => ({ value: item.id, label: itemLabel(item) }));
      binOptions = bins.map((bin) => ({ value: bin.id, label: binLabel(bin) }));
    } catch (err) {
      isError = true;
      message = err instanceof Error ? err.message : 'Failed to load items and bins.';
    } finally {
      isLoadingOptions = false;
    }
  });

  async function handlePickStock() {
    isLoading = true;
    message = '';
    isError = false;

    try {
      const response = await apiFetch('/api/inventory/pick', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        // No performedBy: the API takes that from the session token.
        body: JSON.stringify({
          itemId: Number(itemId),
          warehouseBinId: Number(warehouseBinId),
          quantity: Number(quantity)
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

  <form onsubmit={(e) => { e.preventDefault(); handlePickStock(); }} class="form-grid">
    <div class="input-row">
      <SelectField
        id="pick-item-id"
        label="ITEM"
        options={itemOptions}
        bind:value={itemId}
        placeholder="Select an item"
        emptyLabel={isLoadingOptions ? 'Loading...' : 'No items defined yet'}
        required={true}
      />
      <SelectField
        id="pick-bin-id"
        label="SOURCE BIN"
        options={binOptions}
        bind:value={warehouseBinId}
        placeholder="Select a bin"
        emptyLabel={isLoadingOptions ? 'Loading...' : 'No bins defined yet'}
        required={true}
      />
      <InputField id="pick-quantity" type="number" label="QUANTITY" placeholder="Units to pick" bind:value={quantity} required={true} />
    </div>

    {#if !isLoadingOptions && binOptions.length === 0}
      <div class="alert alert-info">
        No warehouse bins exist yet. Create one on the <a href="/bins">Bins</a> page.
      </div>
    {/if}

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
  .alert-info { background: #e0f2fe; color: #0369a1; border: 1px solid #7dd3fc; }
  .alert-info a { color: inherit; font-weight: 600; }
</style>
