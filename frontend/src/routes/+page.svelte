<script lang="ts">
  import { goto } from '$app/navigation';

  let username = '';
  let password = '';
  let errorMsg = '';

  function handleLogin() {
    if (username.toLowerCase() === 'admin' && password === '123') {
      goto('/admin');
    } else if (username.toLowerCase() === 'employee' && password === '123') {
      goto('/employee');
    } else {
      errorMsg = 'Invalid username or password.';
    }
  }
</script>

<div class="login-wrapper">
  <div class="login-card">
    <div class="logo-header">
      <div class="logo-box">
        <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect><line x1="3" y1="9" x2="21" y2="9"></line><line x1="9" y1="21" x2="9" y2="9"></line></svg>
      </div>
      <h1>Inventria</h1>
      <p>WAREHOUSE MANAGEMENT SYSTEM</p>
    </div>

    <form on:submit|preventDefault={handleLogin}>
      <div class="input-group">
        <label for="username">USERNAME</label>
        <input type="text" id="username" bind:value={username} placeholder="Enter ID" required />
      </div>

      <div class="input-group">
        <div class="label-row">
          <label for="password">PASSWORD</label>
          <a href="#" class="forgot">Forgot?</a>
        </div>
        <input type="password" id="password" bind:value={password} placeholder="••••••••" required />
      </div>

      <div class="checkbox-group">
        <input type="checkbox" id="remember" />
        <label for="remember">Remember this station</label>
      </div>

      {#if errorMsg}
        <p class="error">{errorMsg}</p>
      {/if}

      <button type="submit" class="login-btn">SIGN IN TO DASHBOARD →</button>
    </form>

    <div class="admin-notice">
      <p>🛡️ Secure Admin Access Only</p>
      <p class="sub">If you require access, please contact your Warehouse Administrator to create an account.</p>
    </div>
  </div>
</div>

<style>
  :global(body) { margin: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; background: #f8fafc; color: #333; }
  .login-wrapper { height: 100vh; display: flex; align-items: center; justify-content: center; background: linear-gradient(135deg, #f1f5f9 0%, #e2e8f0 100%); }
  .login-card { background: white; padding: 3rem; border-radius: 12px; box-shadow: 0 10px 25px rgba(0,0,0,0.05); width: 100%; max-width: 400px; }
  .logo-header { text-align: center; margin-bottom: 2.5rem; }
  .logo-box { background: #0b6b36; width: 64px; height: 64px; border-radius: 12px; display: inline-flex; align-items: center; justify-content: center; margin-bottom: 1rem; }
  .logo-header h1 { margin: 0 0 0.5rem 0; color: #0f172a; font-size: 1.8rem; }
  .logo-header p { margin: 0; font-size: 0.75rem; color: #64748b; letter-spacing: 1px; }
  .input-group { margin-bottom: 1.5rem; }
  .label-row { display: flex; justify-content: space-between; }
  label { display: block; font-size: 0.75rem; font-weight: 600; color: #475569; margin-bottom: 0.5rem; letter-spacing: 0.5px; }
  .forgot { font-size: 0.75rem; color: #0b6b36; text-decoration: none; }
  input[type="text"], input[type="password"] { width: 100%; padding: 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px; box-sizing: border-box; outline: none; transition: border 0.2s; }
  input:focus { border-color: #0b6b36; }
  .checkbox-group { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 2rem; }
  .checkbox-group label { margin: 0; font-weight: normal; font-size: 0.85rem; }
  .login-btn { width: 100%; padding: 1rem; background: #0b6b36; color: white; border: none; border-radius: 6px; font-weight: 600; cursor: pointer; transition: background 0.2s; }
  .login-btn:hover { background: #095028; }
  .error { color: #ef4444; font-size: 0.85rem; margin-top: -1rem; margin-bottom: 1rem; text-align: center; }
  .admin-notice { margin-top: 2.5rem; padding-top: 1.5rem; border-top: 1px solid #e2e8f0; text-align: center; }
  .admin-notice p { margin: 0; font-size: 0.85rem; color: #475569; font-weight: 500; }
  .admin-notice .sub { font-size: 0.75rem; color: #94a3b8; margin-top: 0.5rem; line-height: 1.4; }
</style>