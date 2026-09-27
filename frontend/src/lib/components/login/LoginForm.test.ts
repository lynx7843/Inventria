import { render, screen } from '@testing-library/svelte';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const goto = vi.hoisted(() => vi.fn());

vi.mock('$app/environment', () => ({ browser: true }));
vi.mock('$app/navigation', () => ({ goto }));
vi.mock('$app/paths', () => ({ resolve: (path: string) => path }));
vi.mock('$env/dynamic/public', () => ({ env: {} }));

import { getRole } from '$lib/auth';
import LoginForm from './LoginForm.svelte';

const json = (body: unknown, status = 200) =>
	new Response(JSON.stringify(body), {
		status,
		headers: { 'Content-Type': 'application/json' }
	});

async function signIn(username = 'alice', password = 'hunter2') {
	const user = userEvent.setup();
	render(LoginForm);
	await user.type(screen.getByLabelText('USERNAME'), username);
	await user.type(screen.getByLabelText('PASSWORD'), password);
	await user.click(screen.getByRole('button', { name: /sign in/i }));
	return user;
}

describe('LoginForm', () => {
	const fetchMock = vi.fn<typeof fetch>();

	beforeEach(() => {
		goto.mockReset();
		fetchMock.mockReset();
		localStorage.clear();
		vi.stubGlobal('fetch', fetchMock);
	});

	afterEach(() => {
		vi.unstubAllGlobals();
	});

	it.each([
		['Admin', '/admin'],
		['Employee', '/employee']
	])('sends a signed-in %s to %s', async (role, home) => {
		fetchMock.mockResolvedValue(json({ username: 'alice', role }));

		await signIn();

		await vi.waitFor(() => expect(goto).toHaveBeenCalledWith(home));
		expect(getRole()).toBe(role);
	});

	it("shows the API's reason for a rejected sign-in", async () => {
		fetchMock.mockResolvedValue(
			json({ message: 'Too many attempts. Try again in 5 minutes.' }, 429)
		);

		await signIn();

		expect(await screen.findByText('Too many attempts. Try again in 5 minutes.')).toBeVisible();
		expect(screen.getByLabelText('PASSWORD')).toHaveValue('');
		expect(goto).not.toHaveBeenCalled();
	});

	it('falls back to a generic message when the API gives no reason', async () => {
		fetchMock.mockResolvedValue(new Response(null, { status: 401 }));

		await signIn();

		expect(await screen.findByText('Invalid username or password.')).toBeVisible();
	});

	it('refuses a role it has no screen for', async () => {
		fetchMock.mockResolvedValue(json({ username: 'alice', role: 'Auditor' }));

		await signIn();

		expect(await screen.findByText(/unrecognized role \('Auditor'\)/)).toBeVisible();
		expect(getRole()).toBeNull();
		expect(goto).not.toHaveBeenCalled();
	});

	it('asks for a code when the account has two-factor enabled', async () => {
		fetchMock.mockResolvedValueOnce(json({ twoFactorRequired: true, ticket: 't-1' }));

		const user = await signIn();

		const codeBox = await screen.findByLabelText('AUTHENTICATION CODE');
		expect(goto).not.toHaveBeenCalled();

		fetchMock.mockResolvedValueOnce(json({ username: 'alice', role: 'Employee' }));
		await user.type(codeBox, '123456');
		await user.click(screen.getByRole('button', { name: /verify/i }));

		await vi.waitFor(() => expect(goto).toHaveBeenCalledWith('/employee'));
		expect(JSON.parse(fetchMock.mock.calls[1][1]!.body as string)).toEqual({
			ticket: 't-1',
			code: '123456'
		});
	});
});
