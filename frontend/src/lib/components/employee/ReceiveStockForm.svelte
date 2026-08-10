<script lang="ts">
  import InputField from '$lib/components/shared/InputField.svelte';
  import SelectField from '$lib/components/shared/SelectField.svelte';
  import Button from '$lib/components/shared/Button.svelte';
  import { onMount } from 'svelte';
  import { apiFetch, apiErrorMessage } from '$lib/api';
  import { endExpiredSession } from '$lib/auth';
  import { fetchBins, fetchItems, binLabel, itemLabel, parseUnits, type Option } from '$lib/inventory';

  // State variables for the form inputs. The two ids are chosen from what the
  // database actually holds rather than typed: a raw number is something the
  // person at the keyboard has no way to know and every way to get wrong, and a
  // wrong one is stock recorded against the wrong product or shelf.
  let itemId = $state('');
  let warehouseBinId = $state('');
  let quantity = $state('');

  // What the two pickers offer.
  let itemOptions: Option[] = $state([]);
  let binOptions: Option[] = $state([]);
  let isLoadingOptions = $state(true);

  // State variables for UI feedback
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

  async function handleReceiveStock() {
    message = '';
    isError = false;

    // Checked here rather than left to the API. A dropdown with nothing in it is
    // rendered disabled, and a disabled control is exempt from the browser's
    // required-field check, so "no bins exist yet" could still be submitted -
    // as bin 0, which comes back as a complaint about an id nobody chose.
    const units = parseUnits(quantity);

    if (!itemId || !warehouseBinId || units === null) {
      isError = true;
      message = !itemId
        ? 'Choose an item to receive.'
        : !warehouseBinId
          ? 'Choose the bin the stock is going into.'
          : 'Enter the number of units as a whole number greater than zero.';
      return;
    }

    isLoading = true;

    try {
      const response = await apiFetch('/api/inventory/receive', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        // No performedBy: the API takes that from the session token, so a value
        // sent from here would be ignored.
        body: JSON.stringify({
          itemId: Number(itemId),
          warehouseBinId: Number(warehouseBinId),
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

  <form onsubmit={(e) => { e.preventDefault(); handleReceiveStock(); }} class="form-grid">
    <div class="input-row">
      <SelectField
        id="item-id"
        label="ITEM"
        options={itemOptions}
        bind:value={itemId}
        placeholder="Select an item"
        emptyLabel={isLoadingOptions ? 'Loading...' : 'No items defined yet'}
        required={true}
      />
      <SelectField
        id="bin-id"
        label="WAREHOUSE BIN"
        options={binOptions}
        bind:value={warehouseBinId}
        placeholder="Select a bin"
        emptyLabel={isLoadingOptions ? 'Loading...' : 'No bins defined yet'}
        required={true}
      />
      <InputField
        id="quantity"
        type="number"
        label="QUANTITY"
        placeholder="Units received"
        bind:value={quantity}
        required={true}
        min={1}
        step={1}
      />
    </div>

    <!-- Receiving is the first thing anyone tries, and it cannot work until a
         bin exists. Say where to make one instead of offering an empty list. -->
    {#if !isLoadingOptions && binOptions.length === 0}
      <div class="alert alert-info">
        No warehouse bins exist yet. Create one on the <a href="/bins">Bins</a> page before receiving stock.
      </div>
    {:else if !isLoadingOptions && itemOptions.length === 0}
      <div class="alert alert-info">
        No items exist yet. Define one on the <a href="/inventory">Master Inventory</a> page before receiving stock.
      </div>
    {/if}

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
  .alert-info { background: #e0f2fe; color: #0369a1; border: 1px solid #7dd3fc; }
  .alert-info a { color: inherit; font-weight: 600; }
</style>
