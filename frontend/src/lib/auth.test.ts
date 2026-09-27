import { beforeEach, describe, expect, it, vi } from 'vitest';

// `browser` is read on every call, so a getter lets one test switch it off.
const app = vi.hoisted(() => ({ browser: true, goto: vi.fn() }));

vi.mock('$app/environment', () => ({
	get browser() {
		return app.browser;
	}
}));
vi.mock('$app/navigation', () => ({ goto: app.goto }));
vi.mock('$app/paths', () => ({ resolve: (path: string) => path }));

import { clearSession, getRole, getUsername, homeFor, requireSession, saveSession } from './auth';

beforeEach(() => {
	app.browser = true;
	app.goto.mockReset();
	localStorage.clear();
});

describe('homeFor', () => {
	it('sends an Admin to the admin dashboard', () => {
		expect(homeFor('Admin')).toBe('/admin');
	});

	it('sends an Employee to the employee dashboard', () => {
		expect(homeFor('Employee')).toBe('/employee');
	});
});

describe('session storage', () => {
	it('round-trips the username and role', () => {
		saveSession('alice', 'Admin');
		expect(getUsername()).toBe('alice');
		expect(getRole()).toBe('Admin');

		clearSession();
		expect(getUsername()).toBeNull();
		expect(getRole()).toBeNull();
	});

	it('treats a role the API never issues as no role', () => {
		localStorage.setItem('inventria_role', 'Superuser');
		expect(getRole()).toBeNull();
	});

	it('reads nothing during server rendering', () => {
		saveSession('alice', 'Admin');
		app.browser = false;
		expect(getUsername()).toBeNull();
		expect(getRole()).toBeNull();
	});
});

describe('requireSession', () => {
	it('refuses without redirecting during server rendering', () => {
		saveSession('alice', 'Admin');
		app.browser = false;

		expect(requireSession()).toBe(false);
		expect(app.goto).not.toHaveBeenCalled();
	});

	it('sends a visitor with no session to login', () => {
		expect(requireSession()).toBe(false);
		expect(app.goto).toHaveBeenCalledWith('/', { replaceState: true });
	});

	it('clears a tampered role and sends the visitor to login', () => {
		localStorage.setItem('inventria_user', 'mallory');
		localStorage.setItem('inventria_role', 'Superuser');

		expect(requireSession()).toBe(false);
		expect(app.goto).toHaveBeenCalledWith('/', { replaceState: true });
		// The username goes too, so nothing half-signed-in is left behind.
		expect(getUsername()).toBeNull();
	});

	it('lets any signed-in role through when no roles are listed', () => {
		saveSession('bob', 'Employee');

		expect(requireSession()).toBe(true);
		expect(app.goto).not.toHaveBeenCalled();
	});

	it('lets a listed role through', () => {
		saveSession('alice', 'Admin');

		expect(requireSession(['Admin'])).toBe(true);
		expect(app.goto).not.toHaveBeenCalled();
	});

	it('sends an Employee on an Admin-only page to their own home', () => {
		saveSession('bob', 'Employee');

		expect(requireSession(['Admin'])).toBe(false);
		expect(app.goto).toHaveBeenCalledWith('/employee', { replaceState: true });
		// Wrong page, not an ended session: the session stays.
		expect(getRole()).toBe('Employee');
	});

	it('sends an Admin on an Employee-only page to their own home', () => {
		saveSession('alice', 'Admin');

		expect(requireSession(['Employee'])).toBe(false);
		expect(app.goto).toHaveBeenCalledWith('/admin', { replaceState: true });
	});
});
