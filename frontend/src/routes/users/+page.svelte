<script lang="ts">
  import Sidebar from '$lib/components/shared/Sidebar.svelte';
  import Header from '$lib/components/shared/Header.svelte';
  import InputField from '$lib/components/shared/InputField.svelte';
  import Button from '$lib/components/shared/Button.svelte';
  import { onMount } from 'svelte';

  let users = $state([]);
  let isLoading = $state(true);
  let errorMsg = $state('');
  
  // Form State
  let showForm = $state(false);
  let isEditing = $state(false);
  let editingId = $state(null);
  
  let username = $state('');
  let password = $state('');
  let role = $state('Employee'); // Default selection

  async function loadUsers() {
    isLoading = true;
    errorMsg = '';
    try {
      const res = await fetch('http://localhost:5240/api/users');
      if (res.ok) {
        users = await res.json();
      } else {
        throw new Error('Failed to fetch users');
      }
    } catch (err) {
      errorMsg = err instanceof Error ? err.message : 'Network error';
    } finally {
      isLoading = false;
    }
  }

  onMount(loadUsers);

  async function saveUser() {
    errorMsg = '';
    
    // Basic validation
    if (!username) { errorMsg = 'Username is required.'; return; }
    if (!isEditing && !password) { errorMsg = 'Password is required for new users.'; return; }

    const url = isEditing 
      ? `http://localhost:5240/api/users/${editingId}`
      : `http://localhost:5240/api/users`;
    
    const method = isEditing ? 'PUT' : 'POST';

    try {
      const res = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password, role })
      });

      if (res.ok) {
        closeForm();
        await loadUsers();
      } else {
        const data = await res.json();
        errorMsg = data.message || 'Failed to save user.';
      }
    } catch (err) {
      errorMsg = 'A network error occurred while saving.';
    }
  }

  async function deleteUser(id) {
    if (!confirm('WARNING: Are you sure you want to permanently delete this user?')) return;
    
    try {
      const res = await fetch(`http://localhost:5240/api/users/${id}`, { method: 'DELETE' });
      if (res.ok) await loadUsers();
    } catch (err) {
      console.error("Failed to delete user", err);
    }
  }

  function openNewForm() {
    isEditing = false;
    editingId = null;
    username = '';
    password = '';
    role = 'Employee';
    errorMsg = '';
    showForm = true;
  }

  function openEditForm(user) {
    isEditing = true;
    editingId = user.id;
    username = user.username;
    password = ''; // Keep blank so we don't accidentally overwrite unless intended
    role = user.role;
    errorMsg = '';
    showForm = true;
  }

  function closeForm() {
    showForm = false;
  }
</script>

<Sidebar activePage="Users" />
<Header userName="Admin User" role="SYSTEM ROOT" />

<main class="dashboard-content">
  <div class="page-header">
    <div>
      <h2>User Management</h2>
      <p>Control system access, assign roles, and manage employee credentials.</p>
    </div>
    {#if !showForm}
      <button class="btn-solid" onclick={openNewForm}>+ Register New User</button>
    {/if}
  </div>

  {#if showForm}
    <div class="panel form-panel">
      <h3>{isEditing ? 'Edit Existing User' : 'Register New User'}</h3>
      
      {#if errorMsg}
        <div class="alert alert-error">{errorMsg}</div>
      {/if}

      <form onsubmit={(e) => { e.preventDefault(); saveUser(); }} class="form-grid">
        <div class="input-row">
          <InputField id="username" label="USERNAME (EMPLOYEE ID)" placeholder="e.g., emp_105" bind:value={username} required={true} />
          
          <div class="input-group">
            <label for="role">SYSTEM ROLE</label>
            <select id="role" bind:value={role}>
              <option value="Employee">Employee (Warehouse Floor)</option>
              <option value="Admin">Admin (System Manager)</option>
            </select>
          </div>

          <InputField 
            id="password" 
            type="password" 
            label={isEditing ? 'NEW PASSWORD (OPTIONAL)' : 'INITIAL PASSWORD'} 
            placeholder={isEditing ? 'Leave blank to keep current' : '••••••••'} 
            bind:value={password} 
            required={!isEditing} 
          />
        </div>
        <div class="actions">
          <button type="button" class="btn-outline" onclick={closeForm}>Cancel</button>
          <div class="submit-wrap"><Button type="submit" text={isEditing ? 'Update User' : 'Register User'} /></div>
        </div>
      </form>
    </div>
  {/if}

  <div class="panel">
    <table class="data-table">
      <thead>
        <tr>
          <th>INTERNAL ID</th>
          <th>USERNAME</th>
          <th>ROLE</th>
          <th>SECURITY STATUS</th>
          <th class="text-right">ACTIONS</th>
        </tr>
      </thead>
      <tbody>
        {#if isLoading}
          <tr><td colspan="5" class="empty-state">Loading user database...</td></tr>
        {:else if users.length === 0}
          <tr><td colspan="5" class="empty-state">No users found.</td></tr>
        {:else}
          {#each users as user}
            <tr>
              <td class="text-muted">#{user.id}</td>
              <td><strong>{user.username}</strong></td>
              <td>
                <span class="badge {user.role === 'Admin' ? 'admin' : 'employee'}">
                  {user.role}
                </span>
              </td>
              <td><span class="status-dot"></span> BCrypt Secured</td>
              <td class="text-right action-btns">
                <button class="btn-icon edit" onclick={() => openEditForm(user)}>✏️</button>
                <button class="btn-icon delete" onclick={() => deleteUser(user.id)}>🗑️</button>
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
  .input-group label { display: block; font-size: 0.75rem; font-weight: 600; color: #475569; margin-bottom: 0.5rem; letter-spacing: 0.5px; text-transform: uppercase; }
  select { width: 100%; padding: 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px; box-sizing: border-box; outline: none; background: white; font-family: inherit; }
  select:focus { border-color: #0b6b36; }
  
  .actions { display: flex; justify-content: flex-end; gap: 1rem; align-items: center; }
  .submit-wrap { width: 170px; }

  .data-table { width: 100%; border-collapse: collapse; text-align: left; }
  .data-table th { padding: 1rem; border-bottom: 2px solid #e2e8f0; color: #64748b; font-size: 0.75rem; letter-spacing: 0.5px; }
  .data-table td { padding: 1rem; border-bottom: 1px solid #e2e8f0; font-size: 0.9rem; color: #475569; }
  .data-table td strong { color: #0f172a; }
  .empty-state { text-align: center; padding: 3rem; color: #64748b; font-style: italic; }
  
  .text-right { text-align: right; }
  .text-muted { color: #94a3b8; font-size: 0.8rem; }
  
  .badge { padding: 0.25rem 0.6rem; border-radius: 4px; font-size: 0.75rem; font-weight: 600; }
  .badge.admin { background: #fee2e2; color: #991b1b; border: 1px solid #f87171; }
  .badge.employee { background: #e0f2fe; color: #0369a1; border: 1px solid #7dd3fc; }

  .status-dot { display: inline-block; width: 8px; height: 8px; background: #22c55e; border-radius: 50%; margin-right: 0.25rem; }

  .action-btns { display: flex; gap: 0.5rem; justify-content: flex-end; }
  .btn-icon { background: none; border: none; font-size: 1.1rem; cursor: pointer; opacity: 0.6; transition: opacity 0.2s; padding: 0.25rem; }
  .btn-icon:hover { opacity: 1; }
  
  .alert { padding: 0.75rem; border-radius: 6px; font-size: 0.85rem; margin-bottom: 1.5rem; font-weight: 500; }
  .alert-error { background: #fee2e2; color: #991b1b; border: 1px solid #f87171; }
</style>