import { browser } from '$app/environment';
import { goto } from '$app/navigation';

/** The roles the API issues; mirrors `User.Role` on the backend. */
export type Role = 'Admin' | 'Employee';

const USER_KEY = 'inventria_user';
const ROLE_KEY = 'inventria_role';

export function getUsername(): string | null {
	return browser ? localStorage.getItem(USER_KEY) : null;
}

/** The signed-in role, or null when it is absent or not one the API issues. */
export function getRole(): Role | null {
	const role = browser ? localStorage.getItem(ROLE_KEY) : null;
	return role === 'Admin' || role === 'Employee' ? role : null;
}

/**
 * Records who is signed in so pages can lay themselves out for the right role.
 * The session itself is the HttpOnly cookie - none of this is a credential, and
 * removing it signs nobody out.
 */
export function saveSession(username: string, role: Role): void {
	localStorage.setItem(USER_KEY, username);
	localStorage.setItem(ROLE_KEY, role);
}

export function clearSession(): void {
	localStorage.removeItem(USER_KEY);
	localStorage.removeItem(ROLE_KEY);
}

/** Where a role lands after signing in, and where it gets sent back to. */
export function homeFor(role: Role): string {
	return role === 'Admin' ? '/admin' : '/employee';
}

/**
 * Route guard for `onMount`. Returns true when the visitor may stay on the page,
 * or false once a redirect has been started - callers should return immediately
 * on false and render nothing until it returns true.
 *
 * Pass `allowed` to restrict the page to particular roles; omit it to require
 * only that someone is signed in.
 *
 * This is a usability measure, not a security boundary. localStorage is writable
 * by whoever is at the keyboard, so a determined Employee can set the role to
 * Admin and reach this markup. That buys them nothing: every endpoint behind an
 * Admin page is role-checked server-side, so the page loads empty and its writes
 * come back 403. The guard exists so the wrong role sees a sensible screen
 * instead of a broken one.
 */
export function requireSession(allowed?: Role[]): boolean {
	if (!browser) return false;

	const role = getRole();
	if (!role) {
		clearSession();
		goto('/', { replaceState: true });
		return false;
	}

	if (allowed && !allowed.includes(role)) {
		goto(homeFor(role), { replaceState: true });
		return false;
	}

	return true;
}
