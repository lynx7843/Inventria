import { env } from '$env/dynamic/public';

// Base URL of the ASP.NET backend. Set PUBLIC_API_BASE_URL for the target
// environment; the fallback keeps `npm run dev` pointed at a local `dotnet run`
// without anyone needing a .env file. Read at runtime rather than inlined at
// build time, so one build can be promoted across environments.
export const API_BASE_URL = (env.PUBLIC_API_BASE_URL ?? 'http://localhost:5240').replace(/\/+$/, '');

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
