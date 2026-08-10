<script lang="ts">
  import { goto } from '$app/navigation';
  import InputField from '$lib/components/shared/InputField.svelte';
  import Button from '$lib/components/shared/Button.svelte';
  import { apiFetch, apiErrorMessage } from '$lib/api';
  import { saveSession, homeFor } from '$lib/auth';

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
      const response = await apiFetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password })
      });
      
      // A rejected sign-in is not always a wrong password: too many attempts on
      // this account, or from this device, comes back as a 429 saying how long
      // to wait. Hardcoding "Invalid username or password." here hid that and
      // left the form telling people to retype a password that was already
      // right. Fall back to it only when the API said nothing useful.
      if (!response.ok) {
        throw new Error(await apiErrorMessage(response, 'Invalid username or password.'));
      }
      
      // If successful, parse the response data
      const data = await response.json();
      
      // The session token is not here to be saved - it arrived as an HttpOnly
      // cookie the browser attaches on its own. Only the display name and role
      // are kept, and neither is a credential: the role is what lets a page tell
      // an Admin from an Employee, while the API re-checks it on every request.
      // The API whitelists the role when an account is created, so this is only
      // reachable for accounts that predate that check. There is no screen to
      // send them to, so say who can fix it rather than just naming the fault.
      if (data.role !== 'Admin' && data.role !== 'Employee') {
        errorMsg = `Your account has an unrecognized role ('${data.role}'). Ask an administrator to set it to Admin or Employee.`;
        return;
      }

      saveSession(data.username, data.role);
      goto(homeFor(data.role));

    } catch (err) {
      // Catch network errors (like the backend being turned off) or invalid credentials
      errorMsg = err instanceof Error ? err.message : 'Failed to connect to the database.';
    } finally {
      isLoading = false;
    }
  }
</script>

<form onsubmit={(e) => { e.preventDefault(); handleLogin(); }}>
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