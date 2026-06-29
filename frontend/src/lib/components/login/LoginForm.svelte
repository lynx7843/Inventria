<script lang="ts">
  import { goto } from '$app/navigation';
  import InputField from '$lib/components/shared/InputField.svelte';
  import Button from '$lib/components/shared/Button.svelte';

  // Svelte 5 state variables
  let username = $state('');
  let password = $state('');
  let errorMsg = $state('');
  let isLoading = $state(false);

  async function handleLogin() {
    isLoading = true;
    errorMsg = '';

    try {
      await new Promise(resolve => setTimeout(resolve, 800)); 
      
      if (username.toLowerCase() === 'admin' && password === '123') {
        goto('/admin');
      } else if (username.toLowerCase() === 'employee' && password === '123') {
        goto('/employee');
      } else {
        errorMsg = 'Invalid username or password.';
      }
    } catch (err) {
      errorMsg = 'Failed to connect to the database.';
    } finally {
      isLoading = false;
    }
  }
</script>

<form onsubmit={handleLogin}>
  <InputField 
    id="username" 
    label="USERNAME" 
    placeholder="Enter employee ID" 
    bind:value={username} 
    required={true} 
  />

  <div class="password-header">
    <label for="password">PASSWORD</label>
    <a href="#" class="forgot">Forgot?</a>
  </div>
  <InputField 
    id="password" 
    type="password" 
    label="" 
    placeholder="••••••••" 
    bind:value={password} 
    required={true} 
  />

  <div class="checkbox-group">
    <input type="checkbox" id="remember" />
    <label for="remember">Remember this station</label>
  </div>

  {#if errorMsg}
    <p class="error">{errorMsg}</p>
  {/if}

  <Button type="submit" text="SIGN IN TO DASHBOARD →" {isLoading} />
</form>

<style>
  .password-header { display: flex; justify-content: space-between; margin-bottom: -1rem; position: relative; z-index: 1; }
  .password-header label { font-size: 0.75rem; font-weight: 600; color: #475569; letter-spacing: 0.5px; }
  .forgot { font-size: 0.75rem; color: #0b6b36; text-decoration: none; }
  .checkbox-group { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 2rem; margin-top: -0.5rem; }
  .checkbox-group label { margin: 0; font-weight: normal; font-size: 0.85rem; color: #475569; }
  .error { color: #ef4444; font-size: 0.85rem; margin-top: -1rem; margin-bottom: 1rem; text-align: center; }
</style>