<script lang="ts">
  import Sidebar from '$lib/components/shared/Sidebar.svelte';
  import Header from '$lib/components/shared/Header.svelte';
  import InputField from '$lib/components/shared/InputField.svelte';
  import Button from '$lib/components/shared/Button.svelte';
  import { onMount } from 'svelte';

  let items = $state([]);
  let isLoading = $state(true);
  
  // Form State
  let showForm = $state(false);
  let isEditing = $state(false);
  let editingId = $state(null);
  
  let sku = $state('');
  let name = $state('');
  let category = $state('');

  // 1. Fetch All Items (Read)
  async function loadItems() {
    isLoading = true;
    try {
      const token = localStorage.getItem('inventria_token');
      
      const res = await fetch('http://localhost:5240/api/inventory', {
        headers: {
          'Authorization': `Bearer ${token}`
        }
      });
      
      if (res.ok) items = await res.json();
      else if (res.status === 401) {
        window.location.href = '/';
      }
    } catch (err) {
      console.error(err);
    } finally {
      isLoading = false;
    }
  }

  onMount(loadItems);

  // 2. Save Item (Create or Update)
  async function saveItem() {
    const url = isEditing 
      ? `http://localhost:5240/api/inventory/items/${editingId}`
      : `http://localhost:5240/api/inventory/items`;
    
    const method = isEditing ? 'PUT' : 'POST';

    try {
      const res = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sku, name, category })
      });

      if (res.ok) {
        closeForm();
        await loadItems(); // Refresh the table
      }
    } catch (err) {
      console.error("Failed to save item", err);
    }
  }

  // 3. Delete Item
  async function deleteItem(id) {
    if (!confirm('Are you sure you want to delete this item?')) return;
    
    try {
      const res = await fetch(`http://localhost:5240/api/inventory/items/${id}`, {
        method: 'DELETE'
      });
      if (res.ok) await loadItems();
    } catch (err) {
      console.error("Failed to delete", err);
    }
  }

  // UI Helpers
  function openNewForm() {
    isEditing = false;
    editingId = null;
    sku = '';
    name = '';
    category = '';
    showForm = true;
  }

  function openEditForm(item) {
    isEditing = true;
    editingId = item.id;
    sku = item.sku;
    name = item.name;
    category = item.category;
    showForm = true;
  }

  function closeForm() {
    showForm = false;
  }
</script>

<Sidebar activePage="Inventory" />
<Header userName="Admin User" role="SYSTEM ROOT" />

<main class="dashboard-content">
  <div class="page-header">
    <div>
      <h2>Master Inventory</h2>
      <p>Manage product definitions, SKUs, and categories.</p>
    </div>
    {#if !showForm}
      <button class="btn-solid" onclick={openNewForm}>+ Add New SKU</button>
    {/if}
  </div>

  {#if showForm}
    <div class="panel form-panel">
      <h3>{isEditing ? 'Edit Item' : 'Create New Item'}</h3>
      <form onsubmit={(e) => { e.preventDefault(); saveItem(); }} class="form-grid">
        <div class="input-row">
          <InputField id="sku" label="SKU CODE" placeholder="e.g., SKU-100" bind:value={sku} required={true} />
          <InputField id="name" label="PRODUCT NAME" placeholder="e.g., Steel Wrench" bind:value={name} required={true} />
          <InputField id="category" label="CATEGORY" placeholder="e.g., Tools" bind:value={category} required={true} />
        </div>
        <div class="actions">
          <button type="button" class="btn-outline" onclick={closeForm}>Cancel</button>
          <div class="submit-wrap"><Button type="submit" text={isEditing ? 'Update Item' : 'Save Item'} /></div>
        </div>
      </form>
    </div>
  {/if}

  <div class="panel">
    <table class="data-table">
      <thead>
        <tr>
          <th>ID</th>
          <th>ITEM NAME</th>
          <th>SKU</th>
          <th>CATEGORY</th>
          <th class="text-right">ACTIONS</th>
        </tr>
      </thead>
      <tbody>
        {#if isLoading}
          <tr><td colspan="5" class="empty-state">Loading database...</td></tr>
        {:else if items.length === 0}
          <tr><td colspan="5" class="empty-state">No items found. Create one above.</td></tr>
        {:else}
          {#each items as item}
            <tr>
              <td class="text-muted">#{item.id}</td>
              <td><strong>{item.name}</strong></td>
              <td><span class="badge gray">{item.sku}</span></td>
              <td>{item.category}</td>
              <td class="text-right action-btns">
                <button class="btn-icon edit" onclick={() => openEditForm(item)}>✏️</button>
                <button class="btn-icon delete" onclick={() => deleteItem(item.id)}>🗑️</button>
              </td>
            </tr>
          {/each}
        {/if}
      </tbody>
    </table>
  </div>
</main>

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
</style>