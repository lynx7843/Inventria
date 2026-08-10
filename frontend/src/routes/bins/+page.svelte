<script lang="ts">
  import Sidebar from '$lib/components/shared/Sidebar.svelte';
  import Header from '$lib/components/shared/Header.svelte';
  import InputField from '$lib/components/shared/InputField.svelte';
  import Button from '$lib/components/shared/Button.svelte';
  import { onMount } from 'svelte';
  import { apiFetch } from '$lib/api';
  import { requireSession } from '$lib/auth';
  import type { WarehouseBin } from '$lib/inventory';

  // Gates the markup below. No role list: bins are part of the warehouse map
  // that Admins and Employees both maintain, so this only requires a session.
  let allowed = $state(false);

  let bins: WarehouseBin[] = $state([]);
  let isLoading = $state(true);
  let errorMsg = $state('');

  // Form State
  let showForm = $state(false);
  let isEditing = $state(false);
  let editingId: number | null = $state(null);

  let zone = $state('');
  let aisle = $state('');
  let shelf = $state('');

  async function loadBins() {
    isLoading = true;
    try {
      const res = await apiFetch('/api/warehousebins');

      if (res.ok) bins = await res.json();
      else if (res.status === 401) {
        window.location.href = '/';
      }
    } catch (err) {
      console.error(err);
    } finally {
      isLoading = false;
    }
  }

  onMount(() => {
    if (!requireSession()) return;
    allowed = true;
    loadBins();
  });

  async function saveBin() {
    errorMsg = '';

    const path = isEditing
      ? `/api/warehousebins/${editingId}`
      : '/api/warehousebins';

    const method = isEditing ? 'PUT' : 'POST';

    try {
      const res = await apiFetch(path, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ zone, aisle, shelf })
      });

      if (res.ok) {
        closeForm();
        await loadBins();
      } else {
        // Blank fields, an over-long value, or an address another bin already
        // occupies. The server says which.
        const data = await res.json();
        errorMsg = data.message || 'Failed to save bin.';
      }
    } catch (err) {
      errorMsg = 'A network error occurred while saving.';
    }
  }

  async function deleteBin(id: number) {
    if (!confirm('Are you sure you want to delete this bin?')) return;

    errorMsg = '';

    try {
      const res = await apiFetch(`/api/warehousebins/${id}`, {
        method: 'DELETE'
      });
      if (res.ok) {
        await loadBins();
      } else {
        // The server refuses to delete a bin that still holds stock or has
        // movement history recorded against it.
        const data = await res.json();
        errorMsg = data.message || 'Failed to delete bin.';
      }
    } catch (err) {
      errorMsg = 'A network error occurred while deleting.';
    }
  }

  function openNewForm() {
    isEditing = false;
    editingId = null;
    zone = '';
    aisle = '';
    shelf = '';
    errorMsg = '';
    showForm = true;
  }

  function openEditForm(bin: WarehouseBin) {
    isEditing = true;
    editingId = bin.id;
    zone = bin.zone;
    aisle = bin.aisle;
    shelf = bin.shelf;
    errorMsg = '';
    showForm = true;
  }

  function closeForm() {
    showForm = false;
  }
</script>

{#if allowed}
<Sidebar activePage="Bins" />
<Header userName="Admin User" role="SYSTEM ROOT" />

<main class="dashboard-content">
  <div class="page-header">
    <div>
      <h2>Warehouse Bins</h2>
      <p>Define the storage locations stock can be received into, picked from and moved between.</p>
    </div>
    {#if !showForm}
      <button class="btn-solid" onclick={openNewForm}>+ Add New Bin</button>
    {/if}
  </div>

  {#if errorMsg}
    <div class="alert alert-error">{errorMsg}</div>
  {/if}

  {#if showForm}
    <div class="panel form-panel">
      <h3>{isEditing ? 'Edit Bin' : 'Create New Bin'}</h3>
      <form onsubmit={(e) => { e.preventDefault(); saveBin(); }} class="form-grid">
        <div class="input-row">
          <InputField id="zone" label="ZONE" placeholder="e.g., Electronics" bind:value={zone} required={true} />
          <InputField id="aisle" label="AISLE" placeholder="e.g., A1" bind:value={aisle} required={true} />
          <InputField id="shelf" label="SHELF" placeholder="e.g., S3" bind:value={shelf} required={true} />
        </div>
        <div class="actions">
          <button type="button" class="btn-outline" onclick={closeForm}>Cancel</button>
          <div class="submit-wrap"><Button type="submit" text={isEditing ? 'Update Bin' : 'Save Bin'} /></div>
        </div>
      </form>
    </div>
  {/if}

  <div class="panel">
    <table class="data-table">
      <thead>
        <tr>
          <th>ID</th>
          <th>LOCATION</th>
          <th>ZONE</th>
          <th>AISLE</th>
          <th>SHELF</th>
          <th class="text-right">ACTIONS</th>
        </tr>
      </thead>
      <tbody>
        {#if isLoading}
          <tr><td colspan="6" class="empty-state">Loading database...</td></tr>
        {:else if bins.length === 0}
          <tr><td colspan="6" class="empty-state">No bins yet. Create one above - stock cannot be received until a bin exists.</td></tr>
        {:else}
          {#each bins as bin}
            <tr>
              <td class="text-muted">#{bin.id}</td>
              <td><span class="badge gray">{bin.zone}-{bin.aisle}-{bin.shelf}</span></td>
              <td><strong>{bin.zone}</strong></td>
              <td>{bin.aisle}</td>
              <td>{bin.shelf}</td>
              <td class="text-right action-btns">
                <button class="btn-icon edit" onclick={() => openEditForm(bin)}>✏️</button>
                <button class="btn-icon delete" onclick={() => deleteBin(bin.id)}>🗑️</button>
              </td>
            </tr>
          {/each}
        {/if}
      </tbody>
    </table>
  </div>
</main>
{/if}

<style>
  .dashboard-content { margin-left: 250px; padding: 2rem; background: #f8fafc; min-height: calc(100vh - 70px); }
  .page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 2rem; }
  .page-header h2 { margin: 0 0 0.25rem 0; color: #0f172a; }
  .page-header p { margin: 0; color: #64748b; }

  .btn-solid { background: #0b6b36; color: white; border: none; padding: 0.6rem 1.2rem; border-radius: 6px; font-weight: 600; cursor: pointer; }
  .btn-outline { background: white; color: #475569; border: 1px solid #cbd5e1; padding: 0.6rem 1.2rem; border-radius: 6px; font-weight: 600; cursor: pointer; transition: background 0.2s; }
  .btn-outline:hover { background: #f1f5f9; }

  .panel { background: white; padding: 1.5rem; border-radius: 8px; border: 1px solid #e2e8f0; margin-bottom: 2rem; }
  .form-panel h3 { margin: 0 0 1.5rem 0; font-size: 1.1rem; color: #0f172a; border-bottom: 1px solid #e2e8f0; padding-bottom: 0.75rem; }
  .input-row { display: grid; grid-template-columns: repeat(3, 1fr); gap: 1rem; margin-bottom: 1.5rem; }
  .actions { display: flex; justify-content: flex-end; gap: 1rem; align-items: center; }
  .submit-wrap { width: 150px; }

  .data-table { width: 100%; border-collapse: collapse; text-align: left; }
  .data-table th { padding: 1rem; border-bottom: 2px solid #e2e8f0; color: #64748b; font-size: 0.75rem; letter-spacing: 0.5px; }
  .data-table td { padding: 1rem; border-bottom: 1px solid #e2e8f0; font-size: 0.9rem; color: #475569; }
  .data-table td strong { color: #0f172a; }
  .empty-state { text-align: center; padding: 3rem; color: #64748b; font-style: italic; }

  .text-right { text-align: right; }
  .text-muted { color: #94a3b8; font-size: 0.8rem; }
  .badge { padding: 0.25rem 0.5rem; border-radius: 4px; font-size: 0.75rem; font-weight: 600; font-family: monospace; }
  .badge.gray { background: #f1f5f9; color: #475569; border: 1px solid #e2e8f0; }

  .action-btns { display: flex; gap: 0.5rem; justify-content: flex-end; }
  .btn-icon { background: none; border: none; font-size: 1.1rem; cursor: pointer; opacity: 0.6; transition: opacity 0.2s; padding: 0.25rem; }
  .btn-icon:hover { opacity: 1; }

  .alert { padding: 0.75rem; border-radius: 6px; font-size: 0.85rem; margin-bottom: 1.5rem; font-weight: 500; }
  .alert-error { background: #fee2e2; color: #991b1b; border: 1px solid #f87171; }
</style>
