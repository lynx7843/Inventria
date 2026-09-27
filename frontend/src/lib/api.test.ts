import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const endExpiredSession = vi.hoisted(() => vi.fn());

vi.mock('$env/dynamic/public', () => ({ env: {} }));
vi.mock('$lib/auth', () => ({ endExpiredSession }));

import { apiErrorMessage, apiFetch, getJson } from './api';

const json = (body: unknown, status = 200) =>
	new Response(JSON.stringify(body), {
		status,
		headers: { 'Content-Type': 'application/json' }
	});

describe('apiErrorMessage', () => {
	it("uses the API's message when there is one", async () => {
		const res = json({ message: 'SKU-1 is already used by another item.' }, 409);
		expect(await apiErrorMessage(res, 'Failed to save.')).toBe(
			'SKU-1 is already used by another item.'
		);
	});

	it.each([
		['a bare 401 with no body', new Response(null, { status: 401 })],
		['an HTML error page', new Response('<h1>502 Bad Gateway</h1>', { status: 502 })],
		['a JSON body without a message', json({ error: 'nope' }, 500)],
		['a blank message', json({ message: '   ' }, 400)],
		['a message that is not a string', json({ message: 42 }, 400)],
		['a JSON null body', json(null, 500)]
	])('falls back for %s', async (_, res) => {
		expect(await apiErrorMessage(res, 'Failed to save.')).toBe('Failed to save.');
	});
});

describe('apiFetch and getJson', () => {
	const fetchMock = vi.fn<typeof fetch>();

	beforeEach(() => {
		fetchMock.mockReset();
		endExpiredSession.mockReset();
		vi.stubGlobal('fetch', fetchMock);
	});

	afterEach(() => {
		vi.unstubAllGlobals();
	});

	it('sends the session cookie to the API origin', async () => {
		fetchMock.mockResolvedValue(json({}));

		await apiFetch('api/inventory', { method: 'POST' });

		expect(fetchMock).toHaveBeenCalledWith('http://localhost:5240/api/inventory', {
			method: 'POST',
			credentials: 'include'
		});
	});

	it('returns the parsed body of a successful read', async () => {
		fetchMock.mockResolvedValue(json([{ id: 1 }]));

		await expect(getJson('/api/bins', 'bins')).resolves.toEqual([{ id: 1 }]);
	});

	it('ends the session on a 401 instead of reporting a load failure', async () => {
		fetchMock.mockResolvedValue(new Response(null, { status: 401 }));

		await expect(getJson('/api/bins', 'bins')).rejects.toThrow('Your session has expired.');
		expect(endExpiredSession).toHaveBeenCalledOnce();
	});

	it("reports any other failure with the API's message", async () => {
		fetchMock.mockResolvedValue(json({ message: 'Bin 7 does not exist.' }, 404));

		await expect(getJson('/api/bins/7', 'bin')).rejects.toThrow('Bin 7 does not exist.');
		expect(endExpiredSession).not.toHaveBeenCalled();
	});

	it('names what failed to load when the API said nothing', async () => {
		fetchMock.mockResolvedValue(new Response('Internal Server Error', { status: 500 }));

		await expect(getJson('/api/bins', 'bins')).rejects.toThrow('Failed to load bins.');
	});
});
