import { env } from '$env/dynamic/public';

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
