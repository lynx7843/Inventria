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
      // Send the real request to your ASP.NET Core 9 backend
      const response = await fetch('http://localhost:5240/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password })
      });
      
      // If the backend returns a 401 Unauthorized, trigger the error
      if (!response.ok) {
        throw new Error('Invalid username or password.');
      }
      
      // If successful, parse the response data
      const data = await response.json();
      
      // NEW: Save the token and username to localStorage
      localStorage.setItem('inventria_token', data.token);
      localStorage.setItem('inventria_user', data.username);
      
      // Route the user based on the secure role
      if (data.role === 'Admin') {
        goto('/admin');
      } else if (data.role === 'Employee') {
        goto('/employee');
      } else {
        errorMsg = 'Unrecognized user role.';
      }

    } catch (err) {
      // Catch network errors (like the backend being turned off) or invalid credentials
      errorMsg = err instanceof Error ? err.message : 'Failed to connect to the database.';
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
  .password-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.5rem; }
  .password-header label { font-size: 0.75rem; font-weight: 600; color: #475569; letter-spacing: 0.5px; }
  .forgot { font-size: 0.75rem; color: #0b6b36; text-decoration: none; }
  .checkbox-group { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 2rem; margin-top: -0.5rem; }
  .checkbox-group label { margin: 0; font-weight: normal; font-size: 0.85rem; color: #475569; }
  .error { color: #ef4444; font-size: 0.85rem; margin-top: -1rem; margin-bottom: 1rem; text-align: center; }
</style>