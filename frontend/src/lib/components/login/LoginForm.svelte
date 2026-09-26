<script lang="ts">
	import { goto } from '$app/navigation';
	import InputField from '$lib/components/shared/InputField.svelte';
	import Button from '$lib/components/shared/Button.svelte';
	import { apiFetch, apiErrorMessage } from '$lib/api';
	import { saveSession, homeFor, type Role } from '$lib/auth';
	import { focusId } from '$lib/keyboard';

	// Svelte 5 state variables
	let username = $state('');
	let password = $state('');
	let errorMsg = $state('');
	let isLoading = $state(false);

	// The second step, for an account with an authenticator app enrolled. The
	// ticket is what the API gave back instead of a session: it is not a
	// credential for anything else, it is good for one attempt, and it expires
	// in five minutes. Held in a variable rather than storage for exactly that
	// reason - there is nothing here worth surviving a reload, and a reload is
	// the right way to abandon a half-finished sign-in.
	let ticket = $state('');
	let code = $state('');

	// Set after a recovery code is spent, and the one thing that stands between
	// a successful sign-in and the dashboard. Somebody signing in this way has
	// lost their phone and is working through a pile that does not refill
	// itself - being told that while walking past the screen is not being told.
	let recoveryNotice = $state('');

	// The role to route on once that notice is acknowledged. The role rather
	// than the path it resolves to, so the one place that decides where a role
	// lands stays `homeFor`.
	let pendingRole: Role | null = $state(null);

	/**
	 * What both steps do once the API has actually issued a session.
	 *
	 * The role is not a credential - the API re-checks it on every request -
	 * it is only what lets a page tell an Admin from an Employee.
	 */
	function completeSignIn(data: {
		role: string;
		username: string;
		usedRecoveryCode?: boolean;
		recoveryCodesRemaining?: number | null;
	}): boolean {
		// The API whitelists the role when an account is created, so this is only
		// reachable for accounts that predate that check. There is no screen to
		// send them to, so say who can fix it rather than just naming the fault.
		if (data.role !== 'Admin' && data.role !== 'Employee') {
			errorMsg = `Your account has an unrecognized role ('${data.role}'). Ask an administrator to set it to Admin or Employee.`;
			return false;
		}

		saveSession(data.username, data.role);

		const role = data.role as Role;

		if (data.usedRecoveryCode) {
			const left = data.recoveryCodesRemaining ?? 0;
			recoveryNotice =
				left === 0
					? 'That was your last recovery code. Set up your authenticator app again from Settings, or a lost password is the end of this account.'
					: `Recovery code used - you have ${left} left. Set up your authenticator app again from Settings.`;
			pendingRole = role;
			return true;
		}

		goto(homeFor(role));
		return true;
	}

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

			// A 200 that is not a session: the password was right and the account
			// wants a code as well. Nothing has been signed in yet - no cookie was
			// set - so there is nothing to undo if this is abandoned here.
			if (data.twoFactorRequired) {
				ticket = data.ticket;
				password = '';
				focusId('code');
				return;
			}

			// The session token is not here to be saved - it arrived as an HttpOnly
			// cookie the browser attaches on its own. Only the display name and role
			// are kept; see completeSignIn.
			completeSignIn(data);
		} catch (err) {
			// Catch network errors (like the backend being turned off) or invalid credentials
			errorMsg = err instanceof Error ? err.message : 'Failed to connect to the database.';

			// A rejected sign-in almost always means the password, not the
			// username, was wrong - clear it and put the cursor back in it rather
			// than leave focus on the button a retry would otherwise need a click
			// to get past.
			password = '';
			focusId('password');
		} finally {
			isLoading = false;
		}
	}

	async function handleTwoFactor() {
		isLoading = true;
		errorMsg = '';

		try {
			const response = await apiFetch('/api/auth/login/2fa', {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ ticket, code })
			});

			if (!response.ok) {
				throw new Error(await apiErrorMessage(response, 'That code is not valid.'));
			}

			completeSignIn(await response.json());
		} catch (err) {
			errorMsg = err instanceof Error ? err.message : 'Failed to connect to the database.';

			// The ticket is spent whether or not the code was right - one
			// password check buys one attempt, which is what stops six digits
			// being guessed at from a single sign-in. So this goes back to the
			// start rather than leaving a form whose next submission is certain
			// to fail with a more confusing message.
			ticket = '';
			code = '';
			focusId('password');
		} finally {
			isLoading = false;
		}
	}

	function cancelTwoFactor() {
		ticket = '';
		code = '';
		errorMsg = '';
		focusId('password');
	}
</script>

{#if recoveryNotice}
	<!-- Deliberately between a successful sign-in and the dashboard. This is the
	     only moment the person is definitely looking at the screen, and what it
	     says is that the way back in is one code shorter than it was. -->
	<div class="recovery-notice">
		<p class="recovery-title">Signed in with a recovery code</p>
		<p class="recovery-body">{recoveryNotice}</p>
	</div>
	<form
		onsubmit={(e) => {
			e.preventDefault();
			if (pendingRole) goto(homeFor(pendingRole));
		}}
	>
		<Button type="submit" text="CONTINUE →" />
	</form>
{:else if ticket}
	<!-- Step two. The username and password boxes are gone rather than disabled:
	     they have done their job, and leaving them invites a retype that would
	     do nothing. -->
	<form
		onsubmit={(e) => {
			e.preventDefault();
			handleTwoFactor();
		}}
	>
		<p class="two-factor-prompt">
			Enter the six-digit code from your authenticator app. No phone? Use one of your recovery codes
			instead.
		</p>

		<InputField
			id="code"
			label="AUTHENTICATION CODE"
			placeholder="000000"
			bind:value={code}
			required={true}
			autofocus={true}
		/>

		{#if errorMsg}
			<p class="error">{errorMsg}</p>
		{/if}

		<Button type="submit" text="VERIFY →" {isLoading} loadingText="CHECKING..." />

		<button type="button" class="link-back" onclick={cancelTwoFactor} disabled={isLoading}>
			Start over
		</button>
	</form>
{:else}
	<form
		onsubmit={(e) => {
			e.preventDefault();
			handleLogin();
		}}
	>
		<InputField
			id="username"
			label="USERNAME"
			placeholder="Enter employee ID"
			bind:value={username}
			required={true}
			autofocus={true}
		/>

		<!-- "Forgot?" was a link to nowhere. There is no reset flow to send anyone to
       - an Admin sets a new password from the Users screen - so this says that
       instead of offering a click that does nothing. -->
		<div class="password-header">
			<label for="password">PASSWORD</label>
			<span class="forgot">Forgotten? Ask an administrator to reset it</span>
		</div>
		<InputField
			id="password"
			type="password"
			label=""
			placeholder="••••••••"
			bind:value={password}
			required={true}
		/>

		<!-- "Remember this station" is gone rather than wired up. Nothing was reading
       it, and what it promises - a session that outlives the browser being
       closed - is a decision about how long a warehouse terminal stays signed
       in, which belongs to whoever sets the token's eight-hour lifetime, not to
       a checkbox on a shared machine. -->

		{#if errorMsg}
			<p class="error">{errorMsg}</p>
		{/if}

		<Button
			type="submit"
			text="SIGN IN TO DASHBOARD →"
			{isLoading}
			loadingText="AUTHENTICATING..."
		/>
	</form>
{/if}

<style>
	.password-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-bottom: 0.5rem;
	}
	.password-header label {
		font-size: 0.75rem;
		font-weight: 600;
		color: #475569;
		letter-spacing: 0.5px;
	}
	.forgot {
		font-size: 0.75rem;
		color: #94a3b8;
	}
	.error {
		color: #ef4444;
		font-size: 0.85rem;
		margin-top: -1rem;
		margin-bottom: 1rem;
		text-align: center;
	}
	.two-factor-prompt {
		margin: 0 0 1.5rem 0;
		font-size: 0.85rem;
		color: #475569;
		line-height: 1.5;
	}
	.link-back {
		display: block;
		margin: 1rem auto 0;
		background: none;
		border: none;
		padding: 0;
		font-family: inherit;
		font-size: 0.8rem;
		font-weight: 600;
		color: #64748b;
		cursor: pointer;
	}
	.link-back:hover:not(:disabled) {
		text-decoration: underline;
	}
	.link-back:disabled {
		color: #cbd5e1;
		cursor: not-allowed;
	}
	.recovery-notice {
		margin-bottom: 1.5rem;
		padding: 1rem;
		background: #fef3c7;
		border: 1px solid #fcd34d;
		border-radius: 8px;
	}
	.recovery-title {
		margin: 0 0 0.35rem 0;
		font-size: 0.85rem;
		font-weight: 700;
		color: #92400e;
	}
	.recovery-body {
		margin: 0;
		font-size: 0.8rem;
		color: #92400e;
		line-height: 1.5;
	}
</style>
