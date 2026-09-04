import { env } from '$env/dynamic/public';
import { endExpiredSession } from '$lib/auth';

// Base URL of the ASP.NET backend. Set PUBLIC_API_BASE_URL for the target
// environment - including when the backend runs on a scheme or port other than
// the development default below, such as `dotnet run --launch-profile https`,
// where the API also listens on https://localhost:7149. The fallback keeps
// `npm run dev` pointed at a local `dotnet run` without anyone needing a .env
// file. Read at runtime rather than inlined at build time, so one build can be
// promoted across environments.
const DEV_API_BASE_URL = 'http://localhost:5240';

export const API_BASE_URL = (env.PUBLIC_API_BASE_URL || DEV_API_BASE_URL).replace(/\/+$/, '');

/** Builds an absolute API URL from a root-relative path such as `/api/inventory`. */
export function apiUrl(path: string): string {
	return `${API_BASE_URL}${path.startsWith('/') ? path : `/${path}`}`;
}

/**
 * Calls the API with the session cookie attached. The JWT lives in an HttpOnly
 * cookie that script cannot read, so there is no token to put in an
 * Authorization header - `credentials: 'include'` is what authenticates the
 * request, and it is required because the API is on a different origin.
 */
export function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
	return fetch(apiUrl(path), { ...init, credentials: 'include' });
}

/**
 * The sentence to show the user for a failed request.
 *
 * Every error this API returns deliberately carries a `message`, and that
 * sentence is almost always more useful than anything the page could invent -
 * "SKU-1 is already used by another item" rather than "failed to save". But not
 * every failure comes from the API: an expired session is answered by the
 * framework as a bare 401 with no body at all, a crash can arrive as an HTML
 * error page, and a proxy in front of the API can return whatever it likes.
 * Parsing those as JSON throws, and callers that let it throw reported a genuine
 * 401 or 500 as "a network error occurred" - the request reached the server and
 * was answered, so that description was simply wrong.
 */
export async function apiErrorMessage(res: Response, fallback: string): Promise<string> {
	try {
		const data = await res.json();
		if (data && typeof data.message === 'string' && data.message.trim()) {
			return data.message;
		}
	} catch {
		// No JSON body to read; the fallback below is the honest answer.
	}

	return fallback;
}

/**
 * GETs `path` and parses the JSON body, the shape every read-only fetch
 * helper in this app follows: an expired session is sent back to login
 * rather than reported as this particular failure, and any other non-2xx is
 * turned into the message above.
 */
export async function getJson<T>(path: string, what: string): Promise<T> {
	const res = await apiFetch(path);

	if (res.status === 401) {
		// The session is gone. Clearing it matters: the route guards read the
		// stored role, so leaving it behind waves the visitor back onto a page
		// whose every request now fails.
		endExpiredSession();
		throw new Error('Your session has expired.');
	}

	if (!res.ok) throw new Error(await apiErrorMessage(res, `Failed to load ${what}.`));

	return res.json();
}
