<script lang="ts">
	import Sidebar from '$lib/components/shared/Sidebar.svelte';
	import Header from '$lib/components/shared/Header.svelte';
	import { onMount } from 'svelte';
	import { requireSession, getUsername, getRole, saveSession, endExpiredSession } from '$lib/auth';
	import { apiFetch, apiErrorMessage } from '$lib/api';

	// Open to anyone signed in - there's nothing here an Employee shouldn't see
	// about their own account.
	let allowed = $state(false);

	// Prefilled from what's cached locally so the page has a name to show
	// immediately; GET /api/users/me overwrites all four fields below once the
	// account loads, which is the only place any of this - email included -
	// is actually read back from.
	let fullName = $state(getUsername() ?? '');
	const role = getRole() ?? '';
	let email = $state('');
	let notifyLowStock = $state(true);
	let notifyDailySummary = $state(false);
	let isLoadingProfile = $state(true);

	let showSavedFlash = $state(false);
	let savedTimer: ReturnType<typeof setTimeout> | undefined;
	let errorMsg = $state('');
	let saving = $state(false);

	let showPasswordForm = $state(false);
	let currentPassword = $state('');
	let newPassword = $state('');
	let confirmPassword = $state('');
	let passwordError = $state('');
	let passwordSuccess = $state('');
	let changingPassword = $state(false);

	onMount(async () => {
		if (!requireSession()) return;
		allowed = true;

		try {
			const res = await apiFetch('/api/users/me');

			if (res.status === 401) {
				endExpiredSession();
				return;
			}

			if (!res.ok) {
				errorMsg = await apiErrorMessage(res, 'Failed to load your account.');
				return;
			}

			const data = await res.json();
			fullName = data.username;
			email = data.email;
			notifyLowStock = data.notifyLowStock;
			notifyDailySummary = data.notifyDailySummary;
		} catch (err) {
			console.error(err);
			errorMsg = 'A network error occurred while loading your account.';
		} finally {
			isLoadingProfile = false;
		}
	});

	function initials(name: string): string {
		const parts = name.trim().split(/\s+/).filter(Boolean);
		if (parts.length === 0) return '?';
		return (parts[0][0] + (parts[1]?.[0] ?? '')).toUpperCase();
	}

	async function saveChanges() {
		errorMsg = '';
		saving = true;

		try {
			const res = await apiFetch('/api/users/me', {
				method: 'PATCH',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({
					username: fullName,
					email,
					notifyLowStock,
					notifyDailySummary
				})
			});

			if (res.ok) {
				const data = await res.json();
				fullName = data.username;
				email = data.email;
				notifyLowStock = data.notifyLowStock;
				notifyDailySummary = data.notifyDailySummary;
				// The cached username drives layout and greetings elsewhere in the
				// app - it has to move with a rename or those go stale immediately.
				saveSession(data.username, role as 'Admin' | 'Employee');

				clearTimeout(savedTimer);
				showSavedFlash = true;
				savedTimer = setTimeout(() => (showSavedFlash = false), 2500);
				return;
			}

			if (res.status === 401) {
				endExpiredSession();
				return;
			}

			errorMsg = await apiErrorMessage(res, 'Failed to save changes.');
		} catch (err) {
			console.error(err);
			errorMsg = 'A network error occurred while saving.';
		} finally {
			saving = false;
		}
	}

	function togglePasswordForm() {
		showPasswordForm = !showPasswordForm;
		passwordError = '';
		passwordSuccess = '';
		currentPassword = '';
		newPassword = '';
		confirmPassword = '';
	}

	async function changePassword() {
		passwordError = '';
		passwordSuccess = '';

		if (!currentPassword || !newPassword) {
			passwordError = 'Enter your current password and a new one.';
			return;
		}

		// The server enforces this too - BCrypt hashes only the first 72 bytes of
		// whatever it is given, so anything past that does not actually protect
		// the account - but catching it here saves a round trip for the mistake.
		if (newPassword.length > 72) {
			passwordError = 'New password cannot be longer than 72 characters.';
			return;
		}

		if (newPassword !== confirmPassword) {
			passwordError = 'New password and confirmation do not match.';
			return;
		}

		changingPassword = true;

		try {
			const res = await apiFetch('/api/users/me/password', {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ currentPassword, newPassword })
			});

			if (res.ok) {
				passwordSuccess = 'Password changed successfully.';
				currentPassword = '';
				newPassword = '';
				confirmPassword = '';
				return;
			}

			if (res.status === 401) {
				endExpiredSession();
				return;
			}

			// Wrong current password, or a new one that failed validation - the
			// server says which.
			passwordError = await apiErrorMessage(res, 'Failed to change password.');
		} catch (err) {
			console.error(err);
			passwordError = 'A network error occurred while changing your password.';
		} finally {
			changingPassword = false;
		}
	}
</script>

{#if allowed}
	<Sidebar activePage="Settings" />
	<Header />

	<main class="dashboard-content">
		<div class="page-header">
			<h2>Settings</h2>
			<p>Manage your account and notification preferences.</p>
		</div>

		<div class="content-grid">
			<!-- Account Information -->
			<div class="panel">
				<h3 class="panel-title">Account Information</h3>
				<hr class="divider" />
				<div class="acct-content">
					<div class="avatar-col">
						<div class="avatar-wrap">
							<span class="avatar-initials">{initials(fullName)}</span>
						</div>
					</div>

					<div class="fields-col">
						<div class="fields-row">
							<div class="field-wrap">
								<label class="field-label" for="fullName">Username</label>
								<input id="fullName" class="field-input" type="text" bind:value={fullName} />
							</div>
							<div class="field-wrap">
								<label class="field-label" for="role">Role</label>
								<input id="role" class="field-input" type="text" value={role} readonly />
							</div>
						</div>
						<div class="field-wrap">
							<label class="field-label" for="email">Email Address</label>
							<input
								id="email"
								class="field-input"
								type="email"
								placeholder="you@company.com"
								bind:value={email}
							/>
						</div>
					</div>
				</div>

				<div class="panel-footer">
					<hr class="divider" />
					<div class="footer-actions">
						{#if errorMsg}
							<span class="save-error">{errorMsg}</span>
						{/if}
						{#if showSavedFlash}
							<span class="save-flash">
								<svg
									width="14"
									height="14"
									viewBox="0 0 24 24"
									fill="none"
									stroke="#1a6b3c"
									stroke-width="2.5"><polyline points="20 6 9 17 4 12" /></svg
								>
								Changes saved!
							</span>
						{/if}
						<button class="save-btn" onclick={saveChanges} disabled={saving}>
							{saving ? 'Saving…' : 'Save Changes'}
						</button>
					</div>
				</div>
			</div>

			<!-- Right column -->
			<div class="right-col">
				<!-- Security -->
				<div class="panel">
					<h3 class="panel-title">Security</h3>
					<hr class="divider" />
					<div class="sec-items">
						<button
							class="sec-row"
							type="button"
							aria-expanded={showPasswordForm}
							onclick={togglePasswordForm}
						>
							<div class="sec-icon">
								<svg
									width="17"
									height="17"
									viewBox="0 0 24 24"
									fill="none"
									stroke="#6b7280"
									stroke-width="2"
									><rect x="3" y="11" width="18" height="11" rx="2" /><path
										d="M7 11V7a5 5 0 0 1 10 0v4"
									/></svg
								>
							</div>
							<div class="sec-text">
								<span class="sec-label">Change Password</span>
								<span class="sec-sub">Update the password on your account</span>
							</div>
							<span class="chevron" class:open={showPasswordForm}>
								<svg
									width="14"
									height="14"
									viewBox="0 0 24 24"
									fill="none"
									stroke="#94a3b8"
									stroke-width="2.5"><polyline points="6 9 12 15 18 9" /></svg
								>
							</span>
						</button>

						{#if showPasswordForm}
							<form
								class="password-form"
								onsubmit={(e) => {
									e.preventDefault();
									changePassword();
								}}
							>
								<div class="field-wrap">
									<label class="field-label" for="currentPassword">Current Password</label>
									<input
										id="currentPassword"
										class="field-input"
										type="password"
										autocomplete="current-password"
										bind:value={currentPassword}
									/>
								</div>
								<div class="field-wrap">
									<label class="field-label" for="newPassword">New Password</label>
									<input
										id="newPassword"
										class="field-input"
										type="password"
										autocomplete="new-password"
										maxlength="72"
										bind:value={newPassword}
									/>
								</div>
								<div class="field-wrap">
									<label class="field-label" for="confirmPassword">Confirm New Password</label>
									<input
										id="confirmPassword"
										class="field-input"
										type="password"
										autocomplete="new-password"
										maxlength="72"
										bind:value={confirmPassword}
									/>
								</div>

								{#if passwordError}
									<span class="save-error">{passwordError}</span>
								{/if}
								{#if passwordSuccess}
									<span class="save-flash">
										<svg
											width="14"
											height="14"
											viewBox="0 0 24 24"
											fill="none"
											stroke="#1a6b3c"
											stroke-width="2.5"><polyline points="20 6 9 17 4 12" /></svg
										>
										{passwordSuccess}
									</span>
								{/if}

								<div class="password-form-actions">
									<button type="button" class="cancel-btn" onclick={togglePasswordForm}
										>Cancel</button
									>
									<button type="submit" class="save-btn" disabled={changingPassword}>
										{changingPassword ? 'Saving…' : 'Update Password'}
									</button>
								</div>
							</form>
						{/if}

						<div class="sec-row static">
							<div class="sec-icon">
								<svg
									width="17"
									height="17"
									viewBox="0 0 24 24"
									fill="none"
									stroke="#6b7280"
									stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" /></svg
								>
							</div>
							<div class="sec-text">
								<span class="sec-label">Two-Factor Auth</span>
								<span class="sec-sub">Add a second step to signing in</span>
							</div>
							<span class="tag">SOON</span>
						</div>
					</div>
				</div>

				<!-- Notifications -->
				<div class="panel">
					<h3 class="panel-title">Notifications</h3>
					<hr class="divider" />
					<div class="notif-list">
						<div class="notif-row">
							<div class="notif-text">
								<span class="notif-label">Low Stock Email Alerts</span>
								<span class="notif-desc"
									>Immediate notification when a SKU falls below threshold</span
								>
							</div>
							<button
								class="toggle"
								class:on={notifyLowStock}
								role="switch"
								aria-checked={notifyLowStock}
								aria-label="Low Stock Email Alerts"
								disabled={isLoadingProfile}
								onclick={() => (notifyLowStock = !notifyLowStock)}
							>
								<span class="thumb"></span>
							</button>
						</div>
						<div class="notif-row">
							<div class="notif-text">
								<span class="notif-label">Daily Summary Push</span>
								<span class="notif-desc">End of day inventory report via app notification</span>
							</div>
							<button
								class="toggle"
								class:on={notifyDailySummary}
								role="switch"
								aria-checked={notifyDailySummary}
								aria-label="Daily Summary Push"
								disabled={isLoadingProfile}
								onclick={() => (notifyDailySummary = !notifyDailySummary)}
							>
								<span class="thumb"></span>
							</button>
						</div>
					</div>
				</div>
			</div>
		</div>
	</main>
{/if}

<style>
	.dashboard-content {
		margin-left: 250px;
		padding: 2rem;
		background: #f8fafc;
		min-height: calc(100vh - 70px);
	}
	.page-header {
		margin-bottom: 1.5rem;
	}
	.page-header h2 {
		margin: 0 0 0.25rem 0;
		color: #0f172a;
	}
	.page-header p {
		margin: 0;
		color: #64748b;
	}

	.content-grid {
		display: grid;
		grid-template-columns: 1fr 320px;
		gap: 1.5rem;
		align-items: start;
	}

	.panel {
		background: white;
		border: 1px solid #e2e8f0;
		border-radius: 8px;
		padding: 1.5rem;
		display: flex;
		flex-direction: column;
		gap: 1.25rem;
	}
	.panel-title {
		margin: 0;
		font-size: 1.05rem;
		color: #0f172a;
	}
	.divider {
		border: none;
		border-top: 1px solid #e2e8f0;
		margin: 0;
		width: 100%;
	}

	/* Account panel */
	.acct-content {
		display: flex;
		gap: 1.5rem;
		align-items: flex-start;
	}
	.avatar-col {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 0.75rem;
		flex-shrink: 0;
	}
	.avatar-wrap {
		width: 96px;
		height: 96px;
		border-radius: 10px;
		overflow: hidden;
		border: 1px solid #e2e8f0;
		background: #eefdf4;
		display: flex;
		align-items: center;
		justify-content: center;
	}
	.avatar-initials {
		font-size: 1.75rem;
		font-weight: 700;
		color: #0b6b36;
	}

	.fields-col {
		flex: 1;
		display: flex;
		flex-direction: column;
		gap: 1rem;
		min-width: 0;
	}
	.fields-row {
		display: flex;
		gap: 1rem;
	}
	.field-wrap {
		flex: 1;
		display: flex;
		flex-direction: column;
		gap: 0.4rem;
		min-width: 0;
	}
	.field-label {
		font-size: 0.8rem;
		font-weight: 500;
		color: #374151;
	}
	.field-input {
		width: 100%;
		padding: 0.6rem 0.75rem;
		border: 1.5px solid #cbd5e1;
		border-radius: 6px;
		font-size: 0.9rem;
		color: #0f172a;
		background: white;
		outline: none;
		font-family: inherit;
		transition:
			border-color 0.15s,
			box-shadow 0.15s;
	}
	.field-input:focus {
		border-color: #0b6b36;
		box-shadow: 0 0 0 3px rgba(11, 107, 54, 0.1);
	}
	.field-input[readonly] {
		background: #f8fafc;
		color: #64748b;
		cursor: not-allowed;
	}

	.panel-footer {
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}
	.footer-actions {
		display: flex;
		align-items: center;
		justify-content: flex-end;
		gap: 0.75rem;
	}
	.save-btn {
		padding: 0.6rem 1.4rem;
		background: #0b6b36;
		color: white;
		border: none;
		border-radius: 6px;
		font-size: 0.85rem;
		font-weight: 600;
		cursor: pointer;
		font-family: inherit;
		transition: background 0.15s;
	}
	.save-btn:hover {
		background: #095028;
	}
	.save-btn:disabled {
		background: #94a3b8;
		cursor: not-allowed;
	}
	.save-flash {
		display: flex;
		align-items: center;
		gap: 0.35rem;
		font-size: 0.8rem;
		color: #0b6b36;
		font-weight: 500;
	}
	.save-error {
		font-size: 0.8rem;
		color: #b91c1c;
		font-weight: 500;
	}

	/* Right column */
	.right-col {
		display: flex;
		flex-direction: column;
		gap: 1.5rem;
	}

	.sec-items {
		display: flex;
		flex-direction: column;
		gap: 0.6rem;
	}
	.sec-row {
		display: flex;
		align-items: center;
		gap: 0.8rem;
		padding: 0.8rem 0.9rem;
		border: 1.5px solid #e2e8f0;
		border-radius: 8px;
		background: white;
		text-align: left;
		font-family: inherit;
		width: 100%;
		cursor: pointer;
	}
	.sec-row.static {
		cursor: default;
	}
	button.sec-row:hover {
		border-color: #cbd5e1;
	}
	.chevron {
		display: flex;
		flex-shrink: 0;
		transition: transform 0.15s;
	}
	.chevron.open {
		transform: rotate(180deg);
	}

	.password-form {
		display: flex;
		flex-direction: column;
		gap: 1rem;
		padding: 1rem;
		border: 1.5px dashed #e2e8f0;
		border-radius: 8px;
		margin-top: -0.2rem;
	}
	.password-form-actions {
		display: flex;
		justify-content: flex-end;
		gap: 0.75rem;
	}
	.cancel-btn {
		padding: 0.6rem 1.2rem;
		background: white;
		color: #475569;
		border: 1.5px solid #cbd5e1;
		border-radius: 6px;
		font-size: 0.85rem;
		font-weight: 600;
		cursor: pointer;
		font-family: inherit;
	}
	.cancel-btn:hover {
		background: #f1f5f9;
	}
	.sec-icon {
		width: 34px;
		height: 34px;
		border-radius: 8px;
		background: #f1f5f9;
		display: flex;
		align-items: center;
		justify-content: center;
		flex-shrink: 0;
	}
	.sec-text {
		flex: 1;
		display: flex;
		flex-direction: column;
		gap: 0.15rem;
		min-width: 0;
	}
	.sec-label {
		font-size: 0.85rem;
		font-weight: 600;
		color: #0f172a;
	}
	.sec-sub {
		font-size: 0.75rem;
		color: #64748b;
	}
	.tag {
		font-size: 0.6rem;
		font-weight: 700;
		letter-spacing: 0.5px;
		color: #94a3b8;
		background: #f1f5f9;
		border: 1px solid #e2e8f0;
		border-radius: 4px;
		padding: 0.15rem 0.4rem;
		flex-shrink: 0;
	}

	.notif-list {
		display: flex;
		flex-direction: column;
		gap: 1.25rem;
	}
	.notif-row {
		display: flex;
		align-items: flex-start;
		justify-content: space-between;
		gap: 0.9rem;
	}
	.notif-text {
		display: flex;
		flex-direction: column;
		gap: 0.2rem;
		flex: 1;
		min-width: 0;
	}
	.notif-label {
		font-size: 0.85rem;
		font-weight: 600;
		color: #0f172a;
		line-height: 1.3;
	}
	.notif-desc {
		font-size: 0.75rem;
		color: #64748b;
		line-height: 1.4;
	}

	.toggle {
		position: relative;
		width: 42px;
		height: 24px;
		border-radius: 999px;
		border: none;
		background: #cbd5e1;
		cursor: pointer;
		padding: 0;
		flex-shrink: 0;
		transition: background 0.2s;
		margin-top: 1px;
	}
	.toggle:disabled {
		cursor: not-allowed;
		opacity: 0.6;
	}
	.toggle.on {
		background: #0b6b36;
	}
	.thumb {
		position: absolute;
		top: 3px;
		left: 3px;
		width: 18px;
		height: 18px;
		border-radius: 50%;
		background: white;
		box-shadow: 0 1px 3px rgba(0, 0, 0, 0.2);
		transition: transform 0.2s;
		pointer-events: none;
	}
	.toggle.on .thumb {
		transform: translateX(18px);
	}
</style>
