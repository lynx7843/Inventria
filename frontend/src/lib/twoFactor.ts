import { apiFetch, apiErrorMessage } from '$lib/api';
import { endExpiredSession } from '$lib/auth';

/** What `GET /api/users/me/two-factor` says about the signed-in account. */
export type TwoFactorStatus = {
	enabled: boolean;
	/** When it was switched on, or null when it is off. */
	enabledAt: string | null;
	/** Unused recovery codes left. Counts down as they are spent signing in. */
	recoveryCodesRemaining: number;
	/**
	 * Whether this server can hold TOTP secrets at all - false when no
	 * encryption key is configured for it. The page says so rather than
	 * offering a button that always fails.
	 */
	available: boolean;
};

/** What `POST .../enroll` hands back, for the one screen it is shown on. */
export type Enrollment = {
	/** Base32, for typing into an app that cannot scan. */
	secret: string;
	/** The otpauth:// URI the QR code encodes. */
	otpAuthUri: string;
};

/**
 * Calls a two-factor endpoint and returns its parsed body, or throws with the
 * sentence the API sent.
 *
 * Every route here is a POST that either works or explains why, so they all
 * want the same handling - unlike `getJson`, which only reads.
 */
async function post<T>(path: string, body: unknown, whatFailed: string): Promise<T> {
	const res = await apiFetch(path, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(body)
	});

	if (res.status === 401) {
		endExpiredSession();
		throw new Error('Your session has expired.');
	}

	if (!res.ok) throw new Error(await apiErrorMessage(res, whatFailed));

	return res.json();
}

export async function fetchTwoFactorStatus(): Promise<TwoFactorStatus> {
	const res = await apiFetch('/api/users/me/two-factor');

	if (res.status === 401) {
		endExpiredSession();
		throw new Error('Your session has expired.');
	}

	if (!res.ok) throw new Error(await apiErrorMessage(res, 'Failed to load two-factor settings.'));

	return res.json();
}

export function startEnrollment(currentPassword: string): Promise<Enrollment> {
	return post('/api/users/me/two-factor/enroll', { currentPassword }, 'Failed to start setup.');
}

export function confirmEnrollment(code: string): Promise<{ recoveryCodes: string[] }> {
	return post('/api/users/me/two-factor/confirm', { code }, 'Failed to confirm the code.');
}

export function regenerateRecoveryCodes(
	currentPassword: string
): Promise<{ recoveryCodes: string[] }> {
	return post(
		'/api/users/me/two-factor/recovery-codes',
		{ currentPassword },
		'Failed to issue new recovery codes.'
	);
}

export function disableTwoFactor(currentPassword: string): Promise<unknown> {
	return post(
		'/api/users/me/two-factor/disable',
		{ currentPassword },
		'Failed to turn off two-factor authentication.'
	);
}

/**
 * The recovery codes as a text file the browser saves.
 *
 * These are shown exactly once, and "write these down" competes with a person
 * who wants to get back to work. A file they can drop somewhere is the version
 * of that instruction most likely to actually be followed - and printing the
 * page is the other, which the codes are laid out for.
 */
export function downloadRecoveryCodes(codes: string[], username: string) {
	const contents = [
		'Inventria recovery codes',
		`Account: ${username}`,
		`Issued: ${new Date().toISOString()}`,
		'',
		'Each of these signs you in once, in place of your authenticator app.',
		'Keep them somewhere other than the phone that holds the app.',
		'',
		...codes,
		''
	].join('\n');

	const url = URL.createObjectURL(new Blob([contents], { type: 'text/plain' }));

	const link = document.createElement('a');
	link.href = url;
	link.download = `inventria-recovery-codes-${username}.txt`;
	link.click();

	// The blob stays in memory for the life of the document otherwise, and it
	// holds the one copy of these codes that the browser has.
	URL.revokeObjectURL(url);
}
