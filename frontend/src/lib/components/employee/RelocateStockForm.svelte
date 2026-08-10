<script lang="ts">
  import InputField from '$lib/components/shared/InputField.svelte';
  import SelectField from '$lib/components/shared/SelectField.svelte';
  import Button from '$lib/components/shared/Button.svelte';
  import { onMount } from 'svelte';
  import { apiFetch, apiErrorMessage } from '$lib/api';
  import { endExpiredSession } from '$lib/auth';
  import { fetchBins, fetchAllItems, binLabel, itemLabel, parseUnits, type Option } from '$lib/inventory';

  let itemId = $state('');
  let sourceBinId = $state('');
  let destBinId = $state('');
  let quantity = $state('');

  let itemOptions: Option[] = $state([]);
  let binOptions: Option[] = $state([]);
  let isLoadingOptions = $state(true);

  let isLoading = $state(false);
  let message = $state('');
  let isError = $state(false);

  // A move needs somewhere to move to, so a single bin is as useless here as
  // none at all.
  let needsMoreBins = $derived(!isLoadingOptions && binOptions.length < 2);

  // The destination list leaves out whatever the source is set to, so choosing a
  // source that was already the destination would otherwise leave the
  // destination holding an id its own dropdown no longer offers - a form that
  // looks half-filled and submits a move to the bin it came from.
  $effect(() => {
    if (destBinId && destBinId === sourceBinId) destBinId = '';
  });

  onMount(async () => {
    try {
      const [items, bins] = await Promise.all([fetchAllItems(), fetchBins()]);
      itemOptions = items.map((item) => ({ value: item.id, label: itemLabel(item) }));
      binOptions = bins.map((bin) => ({ value: bin.id, label: binLabel(bin) }));
    } catch (err) {
      isError = true;
      message = err instanceof Error ? err.message : 'Failed to load items and bins.';
    } finally {
      isLoadingOptions = false;
    }
  });

  async function handleRelocateStock() {
    message = '';
    isError = false;

    // See ReceiveStockForm: an empty dropdown is a disabled one, which the
    // browser's required check skips.
    const units = parseUnits(quantity);

    if (!itemId || !sourceBinId || !destBinId || units === null) {
      isError = true;
      message = !itemId
        ? 'Choose an item to move.'
        : !sourceBinId
          ? 'Choose the bin the stock is coming from.'
          : !destBinId
            ? 'Choose the bin the stock is going to.'
            : 'Enter the number of units as a whole number greater than zero.';
      return;
    }

    isLoading = true;

    try {
      const response = await apiFetch('/api/inventory/relocate', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        // No performedBy: the API takes that from the session token.
        body: JSON.stringify({
          itemId: Number(itemId),
          sourceBinId: Number(sourceBinId),
          destinationBinId: Number(destBinId),
          quantity: units
        })
      });

      // Status first: a 401 is answered with no body at all, so parsing before
      // this point turned an expired session into "a network error occurred".
      if (response.status === 401) {
        endExpiredSession();
        return;
      }

      if (!response.ok) {
        throw new Error(await apiErrorMessage(response, 'Failed to process transaction.'));
      }

      const data = await response.json();

      message = data.message;
      itemId = '';
      sourceBinId = '';
      destBinId = '';
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
    <h3>Internal Relocation</h3>
    <p class="subtitle">Move stock from one warehouse bin to another.</p>
  </div>

  <form onsubmit={(e) => { e.preventDefault(); handleRelocateStock(); }} class="form-grid">
    <div class="input-row">
      <SelectField
        id="rel-item-id"
        label="ITEM"
        options={itemOptions}
        bind:value={itemId}
        placeholder="Select an item"
        emptyLabel={isLoadingOptions ? 'Loading...' : 'No items defined yet'}
        required={true}
      />
      <SelectField
        id="rel-source-bin"
        label="SOURCE BIN"
        options={binOptions}
        bind:value={sourceBinId}
        placeholder="Select a bin"
        emptyLabel={isLoadingOptions ? 'Loading...' : 'No bins defined yet'}
        required={true}
      />
      <SelectField
        id="rel-dest-bin"
        label="DESTINATION BIN"
        options={binOptions.filter((bin) => String(bin.value) !== sourceBinId)}
        bind:value={destBinId}
        placeholder="Select a bin"
        emptyLabel={isLoadingOptions ? 'Loading...' : 'No other bin available'}
        required={true}
      />
      <InputField id="rel-qty" type="number" label="QUANTITY" placeholder="Units to move" bind:value={quantity} required={true} min={1} step={1} />
    </div>

    {#if needsMoreBins}
      <div class="alert alert-info">
        Relocating needs two bins to move between. Create them on the <a href="/bins">Bins</a> page.
      </div>
    {/if}

    {#if message}
      <div class="alert" class:alert-error={isError} class:alert-success={!isError}>
        {message}
      </div>
    {/if}

    <div class="submit-row">
      <Button type="submit" text="MOVE STOCK" {isLoading} loadingText="MOVING STOCK..." />
    </div>
  </form>
</div>

<style>
  .form-panel { background: white; padding: 1.5rem; border-radius: 8px; border: 1px solid #e2e8f0; margin-bottom: 2rem; }
  .panel-header { margin-bottom: 1.5rem; }
  .panel-header h3 { margin: 0 0 0.25rem 0; font-size: 1.1rem; color: #0f172a; }
  .subtitle { margin: 0; font-size: 0.85rem; color: #64748b; }
  .input-row { display: grid; grid-template-columns: repeat(4, 1fr); gap: 1rem; }
  .submit-row { display: flex; justify-content: flex-end; margin-top: 1rem; width: 200px; margin-left: auto; }

  .alert { padding: 0.75rem; border-radius: 6px; font-size: 0.85rem; margin-top: 1rem; font-weight: 500; }
  .alert-error { background: #fee2e2; color: #991b1b; border: 1px solid #f87171; }
  .alert-success { background: #dcfce7; color: #166534; border: 1px solid #4ade80; }
  .alert-info { background: #e0f2fe; color: #0369a1; border: 1px solid #7dd3fc; }
  .alert-info a { color: inherit; font-weight: 600; }
</style>
