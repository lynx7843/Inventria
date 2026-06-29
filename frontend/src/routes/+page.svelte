<script lang="ts">
  import LoginForm from '$lib/components/login/LoginForm.svelte';
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

    <LoginForm />

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
  .admin-notice { margin-top: 2.5rem; padding-top: 1.5rem; border-top: 1px solid #e2e8f0; text-align: center; }
  .admin-notice p { margin: 0; font-size: 0.85rem; color: #475569; font-weight: 500; }
  .admin-notice .sub { font-size: 0.75rem; color: #94a3b8; margin-top: 0.5rem; line-height: 1.4; }
</style>