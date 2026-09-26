<script lang="ts">
	import Sidebar from '$lib/components/shared/Sidebar.svelte';
	import Header from '$lib/components/shared/Header.svelte';
	import { onMount } from 'svelte';
	import { requireSession, getUsername, getRole, saveSession, endExpiredSession } from '$lib/auth';
	import { apiFetch, apiErrorMessage, apiUrl } from '$lib/api';
	import QrCode from '$lib/components/shared/QrCode.svelte';
	import {
		confirmEnrollment,
		disableTwoFactor,
		downloadRecoveryCodes,
		fetchTwoFactorStatus,
		regenerateRecoveryCodes,
		startEnrollment,
		type Enrollment,
		type TwoFactorStatus
	} from '$lib/twoFactor';

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

	// Root-relative, e.g. "/uploads/avatars/3f2b....jpg" - apiUrl() turns it
	// into a full URL against the backend, which is a different origin than
	// this page. Null shows the initials avatar below instead.
	let avatarUrl = $state<string | null>(null);
	let avatarInput = $state<HTMLInputElement | undefined>(undefined);
	let uploadingAvatar = $state(false);
	let avatarError = $state('');

	const AVATAR_MAX_BYTES = 2 * 1024 * 1024;
	const AVATAR_TYPES = new Set(['image/jpeg', 'image/png', 'image/webp']);

	let showSavedFlash = $state(false);
	let savedTimer: ReturnType<typeof setTimeout> | undefined;
	let errorMsg = $state('');
	let saving = $state(false);

	// --- TWO-FACTOR ---------------------------------------------------------
	//
	// Four screens in one panel, because that is what turning this on actually
	// is: decide, scan, prove it scanned, and write down the way back in. The
	// step is held here rather than inferred from which fields are filled, so a
	// half-finished setup cannot be mistaken for a finished one.
	type TwoFactorStep = 'idle' | 'password' | 'scan' | 'codes';

	let twoFactor: TwoFactorStatus | null = $state(null);
	let twoFactorStep: TwoFactorStep = $state('idle');
	let twoFactorPassword = $state('');
	let twoFactorCode = $state('');
	let twoFactorError = $state('');
	let twoFactorBusy = $state(false);
	let enrollment: Enrollment | null = $state(null);
	let recoveryCodes: string[] = $state([]);

	// What the password step is being asked for: the same box confirms turning
	// it on, turning it off, and reissuing codes, and only the button underneath
	// differs.
	let passwordPurpose: 'enable' | 'disable' | 'regenerate' = $state('enable');

	// Shown above the codes, because reaching this screen for those two reasons
	// means something different: one is "you are finished", the other is "the
	// codes you had are now dead".
	let codesAreReissued = $state(false);

	async function loadTwoFactor() {
		try {
			twoFactor = await fetchTwoFactorStatus();
		} catch (err) {
			console.error(err);
			twoFactorError = err instanceof Error ? err.message : 'Failed to load two-factor settings.';
		}
	}

	function openTwoFactorPassword(purpose: 'enable' | 'disable' | 'regenerate') {
		passwordPurpose = purpose;
		twoFactorPassword = '';
		twoFactorCode = '';
		twoFactorError = '';
		twoFactorStep = 'password';
	}

	function closeTwoFactor() {
		twoFactorStep = 'idle';
		twoFactorPassword = '';
		twoFactorCode = '';
		twoFactorError = '';

		// The codes are dropped from memory with the screen that showed them.
		// Leaving them in a variable the panel could be reopened onto would turn
		// "shown once" into "shown whenever".
		enrollment = null;
		recoveryCodes = [];
	}

	async function submitTwoFactorPassword() {
		twoFactorError = '';
		twoFactorBusy = true;

		try {
			if (passwordPurpose === 'enable') {
				enrollment = await startEnrollment(twoFactorPassword);
				twoFactorStep = 'scan';
			} else if (passwordPurpose === 'regenerate') {
				recoveryCodes = (await regenerateRecoveryCodes(twoFactorPassword)).recoveryCodes;
				codesAreReissued = true;
				twoFactorStep = 'codes';
				await loadTwoFactor();
			} else {
				await disableTwoFactor(twoFactorPassword);
				await loadTwoFactor();
				closeTwoFactor();
			}
		} catch (err) {
			twoFactorError = err instanceof Error ? err.message : 'Something went wrong.';
		} finally {
			twoFactorBusy = false;
			twoFactorPassword = '';
		}
	}

	async function submitTwoFactorCode() {
		twoFactorError = '';
		twoFactorBusy = true;

		try {
			recoveryCodes = (await confirmEnrollment(twoFactorCode)).recoveryCodes;
			codesAreReissued = false;
			enrollment = null;
			twoFactorStep = 'codes';
			await loadTwoFactor();
		} catch (err) {
			twoFactorError = err instanceof Error ? err.message : 'Something went wrong.';
		} finally {
			twoFactorBusy = false;
			twoFactorCode = '';
		}
	}

	/** Base32 in groups of four, which is how it gets read into a phone. */
	function formatSecret(secret: string): string {
		return (secret.match(/.{1,4}/g) ?? [secret]).join(' ');
	}

	function formatEnabledAt(iso: string | null): string {
		return iso ? new Date(iso).toLocaleDateString() : '';
	}

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

		loadTwoFactor();

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
			avatarUrl = data.avatarUrl ?? null;
		} catch (err) {
			console.error(err);
			errorMsg = 'A network error occurred while loading your account.';
		} finally {
			isLoadingProfile = false;
		}
	});

	function pickPhoto() {
		avatarError = '';
		avatarInput?.click();
	}

	async function onPhotoChosen(event: Event) {
		const input = event.currentTarget as HTMLInputElement;
		const file = input.files?.[0];
		// Clearing this lets the same file be re-picked after a rejected upload -
		// without it, choosing the identical file twice in a row fires no change
		// event the second time.
		input.value = '';
		if (!file) return;

		avatarError = '';

		// The same checks the server makes, run here only to save a round trip
		// on the common mistake - the server's checks are the ones that actually
		// decide what gets saved.
		if (!AVATAR_TYPES.has(file.type)) {
			avatarError = 'File must be a JPEG, PNG, or WebP image.';
			return;
		}
		if (file.size > AVATAR_MAX_BYTES) {
			avatarError = 'Image must be 2 MB or smaller.';
			return;
		}

		uploadingAvatar = true;

		try {
			const body = new FormData();
			body.append('file', file);
			const res = await apiFetch('/api/users/me/avatar', { method: 'POST', body });

			if (res.status === 401) {
				endExpiredSession();
				return;
			}

			if (!res.ok) {
				avatarError = await apiErrorMessage(res, 'Failed to upload photo.');
				return;
			}

			const data = await res.json();
			avatarUrl = data.avatarUrl ?? null;
		} catch (err) {
			console.error(err);
			avatarError = 'A network error occurred while uploading.';
		} finally {
			uploadingAvatar = false;
		}
	}

	async function removePhoto() {
		avatarError = '';
		uploadingAvatar = true;

		try {
			const res = await apiFetch('/api/users/me/avatar', { method: 'DELETE' });

			if (res.status === 401) {
				endExpiredSession();
				return;
			}

			if (!res.ok) {
				avatarError = await apiErrorMessage(res, 'Failed to remove photo.');
				return;
			}

			avatarUrl = null;
		} catch (err) {
			console.error(err);
			avatarError = 'A network error occurred while removing the photo.';
		} finally {
			uploadingAvatar = false;
		}
	}

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
							{#if avatarUrl}
								<img class="avatar-photo" src={apiUrl(avatarUrl)} alt="" />
							{:else}
								<span class="avatar-initials">{initials(fullName)}</span>
							{/if}
						</div>

						<input
							bind:this={avatarInput}
							type="file"
							accept="image/jpeg,image/png,image/webp"
							class="avatar-file-input"
							onchange={onPhotoChosen}
						/>

						<div class="avatar-actions">
							<button
								type="button"
								class="avatar-link-btn"
								onclick={pickPhoto}
								disabled={uploadingAvatar}
							>
								{uploadingAvatar ? 'Uploading…' : avatarUrl ? 'Change Photo' : 'Upload Photo'}
							</button>
							{#if avatarUrl}
								<button
									type="button"
									class="avatar-link-btn danger"
									onclick={removePhoto}
									disabled={uploadingAvatar}
								>
									Remove
								</button>
							{/if}
						</div>

						{#if avatarError}
							<span class="save-error avatar-error">{avatarError}</span>
						{/if}
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

						<div class="sec-row" class:static={!twoFactor?.available}>
							<div class="sec-icon">
								<svg
									width="17"
									height="17"
									viewBox="0 0 24 24"
									fill="none"
									stroke={twoFactor?.enabled ? '#0b6b36' : '#6b7280'}
									stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" /></svg
								>
							</div>
							<div class="sec-text">
								<span class="sec-label">Two-Factor Auth</span>
								<span class="sec-sub">
									{#if twoFactor === null}
										Loading…
									{:else if !twoFactor.available}
										Not configured on this server
									{:else if twoFactor.enabled}
										On since {formatEnabledAt(twoFactor.enabledAt)} &middot;
										{twoFactor.recoveryCodesRemaining} recovery code{twoFactor.recoveryCodesRemaining ===
										1
											? ''
											: 's'} left
									{:else}
										A code from your phone, as well as your password
									{/if}
								</span>
							</div>

							{#if twoFactor === null}
								<span class="tag">…</span>
							{:else if !twoFactor.available}
								<!-- The server has no key to encrypt a TOTP secret with, so
								     enrolling would have to either store it in the clear or
								     fail at the last step. Say which rather than showing a
								     button that cannot work. -->
								<span class="tag">UNAVAILABLE</span>
							{:else if twoFactor.enabled}
								<span class="tag on">ON</span>
							{:else}
								<button
									class="sec-action"
									type="button"
									onclick={() => openTwoFactorPassword('enable')}
									disabled={twoFactorStep !== 'idle'}
								>
									Set up
								</button>
							{/if}
						</div>

						{#if twoFactor?.enabled && twoFactorStep === 'idle'}
							<div class="tfa-manage">
								<button
									class="link-action"
									type="button"
									onclick={() => openTwoFactorPassword('regenerate')}
								>
									New recovery codes
								</button>
								<button
									class="link-action danger"
									type="button"
									onclick={() => openTwoFactorPassword('disable')}
								>
									Turn off
								</button>
							</div>
						{/if}

						{#if twoFactor?.enabled && twoFactor.recoveryCodesRemaining <= 2 && twoFactorStep === 'idle'}
							<!-- Running out is not an error yet, but it is the last moment
							     it can be fixed without already being locked out. -->
							<p class="tfa-warning">
								{twoFactor.recoveryCodesRemaining === 0
									? 'You have no recovery codes left. Without one, a lost phone locks you out of this account.'
									: 'You are nearly out of recovery codes. Issue a new set while you can still sign in.'}
							</p>
						{/if}

						{#if twoFactorStep === 'password'}
							<form
								class="password-form"
								onsubmit={(e) => {
									e.preventDefault();
									submitTwoFactorPassword();
								}}
							>
								<p class="tfa-step-text">
									{#if passwordPurpose === 'enable'}
										Confirm your password to start setting up your authenticator app.
									{:else if passwordPurpose === 'regenerate'}
										Confirm your password. Your current recovery codes will stop working.
									{:else}
										Confirm your password to turn off two-factor authentication. Your recovery codes
										will be deleted.
									{/if}
								</p>
								<div class="field-wrap">
									<label class="field-label" for="tfaPassword">Current Password</label>
									<!-- svelte-ignore a11y_autofocus -->
									<input
										id="tfaPassword"
										class="field-input"
										type="password"
										autocomplete="current-password"
										bind:value={twoFactorPassword}
										autofocus
									/>
								</div>

								{#if twoFactorError}
									<span class="save-error">{twoFactorError}</span>
								{/if}

								<div class="password-form-actions">
									<button type="button" class="cancel-btn" onclick={closeTwoFactor}>Cancel</button>
									<button
										type="submit"
										class="save-btn"
										class:danger={passwordPurpose === 'disable'}
										disabled={twoFactorBusy || !twoFactorPassword}
									>
										{#if twoFactorBusy}
											Working…
										{:else if passwordPurpose === 'enable'}
											Continue
										{:else if passwordPurpose === 'regenerate'}
											Issue new codes
										{:else}
											Turn off
										{/if}
									</button>
								</div>
							</form>
						{/if}

						{#if twoFactorStep === 'scan' && enrollment}
							<form
								class="password-form"
								onsubmit={(e) => {
									e.preventDefault();
									submitTwoFactorCode();
								}}
							>
								<p class="tfa-step-text">
									Scan this with Google Authenticator, 1Password, or any authenticator app, then
									enter the six digits it shows.
								</p>

								<div class="tfa-scan">
									<QrCode
										value={enrollment.otpAuthUri}
										size={168}
										label="Authenticator setup code"
									/>
									<div class="tfa-manual">
										<span class="field-label">Can't scan?</span>
										<p class="tfa-hint">Enter this key into the app by hand:</p>
										<code class="tfa-secret">{formatSecret(enrollment.secret)}</code>
									</div>
								</div>

								<div class="field-wrap">
									<label class="field-label" for="tfaCode">Six-digit code</label>
									<!-- inputmode and the pattern get a numeric keypad on a phone,
									     which is where the digits are being read from anyway.
									     one-time-code lets a password manager fill it. -->
									<!-- svelte-ignore a11y_autofocus -->
									<input
										id="tfaCode"
										class="field-input code-input"
										type="text"
										inputmode="numeric"
										pattern="[0-9]*"
										maxlength="6"
										autocomplete="one-time-code"
										placeholder="000000"
										bind:value={twoFactorCode}
										autofocus
									/>
								</div>

								{#if twoFactorError}
									<span class="save-error">{twoFactorError}</span>
								{/if}

								<div class="password-form-actions">
									<button type="button" class="cancel-btn" onclick={closeTwoFactor}>Cancel</button>
									<button
										type="submit"
										class="save-btn"
										disabled={twoFactorBusy || twoFactorCode.length !== 6}
									>
										{twoFactorBusy ? 'Checking…' : 'Turn on'}
									</button>
								</div>
							</form>
						{/if}

						{#if twoFactorStep === 'codes'}
							<div class="password-form">
								<p class="tfa-step-text">
									{#if codesAreReissued}
										<strong>New recovery codes.</strong> The previous set no longer works.
									{:else}
										<strong>Two-factor authentication is on.</strong>
									{/if}
									Each code below signs you in once if you lose your phone. This is the only time they
									are shown.
								</p>

								<ul class="tfa-codes">
									{#each recoveryCodes as code (code)}
										<li>{code}</li>
									{/each}
								</ul>

								<div class="password-form-actions">
									<button
										type="button"
										class="cancel-btn"
										onclick={() => downloadRecoveryCodes(recoveryCodes, fullName)}
									>
										Download
									</button>
									<button type="button" class="save-btn" onclick={closeTwoFactor}>
										I've saved these
									</button>
								</div>
							</div>
						{/if}
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
	.avatar-photo {
		width: 100%;
		height: 100%;
		object-fit: cover;
	}
	.avatar-file-input {
		/* Visually hidden but still focusable/clickable via the button above,
		   rather than display:none which would drop it from the tab order. */
		position: absolute;
		width: 1px;
		height: 1px;
		padding: 0;
		margin: -1px;
		overflow: hidden;
		clip: rect(0, 0, 0, 0);
		white-space: nowrap;
		border: 0;
	}
	.avatar-actions {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 0.35rem;
	}
	.avatar-link-btn {
		background: none;
		border: none;
		padding: 0;
		font-size: 0.78rem;
		font-weight: 600;
		color: #0b6b36;
		cursor: pointer;
		font-family: inherit;
	}
	.avatar-link-btn:hover {
		text-decoration: underline;
	}
	.avatar-link-btn:disabled {
		color: #94a3b8;
		cursor: not-allowed;
		text-decoration: none;
	}
	.avatar-link-btn.danger {
		color: #b91c1c;
	}
	.avatar-error {
		max-width: 120px;
		text-align: center;
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
		/* Without this, width:100% is the content box and the padding and border
		   are added on top - so every field inside the security panel's dashed
		   form (this one and the password form beside it) ran a few pixels past
		   the box it sits in. */
		box-sizing: border-box;
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
	.tag.on {
		color: #166534;
		background: #dcfce7;
		border-color: #4ade80;
	}

	/* --- Two-factor --- */
	.sec-action {
		padding: 0.4rem 0.9rem;
		background: white;
		color: #0b6b36;
		border: 1.5px solid #cbd5e1;
		border-radius: 6px;
		font-size: 0.78rem;
		font-weight: 600;
		font-family: inherit;
		cursor: pointer;
		flex-shrink: 0;
	}
	.sec-action:hover:not(:disabled) {
		border-color: #0b6b36;
		background: #f0fdf4;
	}
	.sec-action:disabled {
		color: #94a3b8;
		cursor: not-allowed;
	}
	.tfa-manage {
		display: flex;
		gap: 1rem;
		padding: 0 0.2rem;
		margin-top: -0.2rem;
	}
	.link-action {
		background: none;
		border: none;
		padding: 0;
		font-family: inherit;
		font-size: 0.75rem;
		font-weight: 600;
		color: #0b6b36;
		cursor: pointer;
	}
	.link-action:hover {
		text-decoration: underline;
	}
	.link-action.danger {
		color: #b91c1c;
	}
	.tfa-warning {
		margin: 0;
		padding: 0.6rem 0.75rem;
		background: #fef3c7;
		border: 1px solid #fcd34d;
		border-radius: 6px;
		font-size: 0.75rem;
		color: #92400e;
	}
	.tfa-step-text {
		margin: 0;
		font-size: 0.8rem;
		color: #475569;
		line-height: 1.5;
	}
	/* Stacked, not side by side. The security panel is the narrow column of a
	   two-column page, and putting a 168px QR next to the fallback key left the
	   key three characters wide - which is the one thing on this screen somebody
	   has to read accurately, character by character, into a phone. */
	.tfa-scan {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 0.8rem;
	}
	.tfa-manual {
		display: flex;
		flex-direction: column;
		gap: 0.3rem;
		width: 100%;
		min-width: 0;
	}
	.tfa-hint {
		margin: 0;
		font-size: 0.75rem;
		color: #64748b;
	}
	.tfa-secret {
		font-family: monospace;
		font-size: 0.78rem;
		/* Grouped by eye rather than run together: this is read aloud or typed
		   one character at a time, and base32 in an unbroken 32-character line
		   is where people lose their place. */
		letter-spacing: 2px;
		word-spacing: 2px;
		line-height: 1.6;
		text-align: center;
		color: #0f172a;
		background: #f1f5f9;
		border: 1px solid #e2e8f0;
		border-radius: 6px;
		padding: 0.5rem 0.6rem;
		/* Base32 in one unbroken run is wider than this column; wrapping it
		   beats a scrollbar on something being copied by eye. */
		overflow-wrap: anywhere;
	}
	.code-input {
		font-family: monospace;
		font-size: 1.1rem;
		letter-spacing: 0.4em;
		text-align: center;
	}
	/* One per line. Two columns fits more on screen, but in the narrow security
	   panel it wrapped each code across two lines - and a code broken mid-group
	   is a code somebody transcribes wrongly. */
	.tfa-codes {
		display: grid;
		grid-template-columns: 1fr;
		gap: 0.35rem;
		list-style: none;
		margin: 0;
		padding: 0.8rem;
		background: #f8fafc;
		border: 1px solid #e2e8f0;
		border-radius: 8px;
		font-family: monospace;
		font-size: 0.8rem;
		color: #0f172a;
	}
	.save-btn.danger {
		background: #b91c1c;
	}
	.save-btn.danger:hover {
		background: #991b1b;
	}

	@media print {
		/* Printing the page is one of the two ways someone saves these, and the
		   panels around them are not worth the paper. */
		.tfa-codes {
			font-size: 11pt;
		}
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
