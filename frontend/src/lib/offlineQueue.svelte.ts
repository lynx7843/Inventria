import { apiFetch, apiErrorMessage } from '$lib/api';
import { endExpiredSession } from '$lib/auth';

/**
 * A stock movement (receive/pick/relocate) that could not reach the API - a
 * wifi dead zone, not a rejection - held here to replay once the connection
 * comes back.
 *
 * 'conflict' is terminal: see submitMovementOrQueue and replayQueue for why a
 * conflicted item is never retried automatically again. It sits here, visible
 * in OfflineQueueBanner, until someone looks at it and chooses to retry or
 * discard it.
 */
export type QueuedMovement = {
	id: string;
	endpoint: string;
	body: Record<string, unknown>;
	/** What to show a person - "Receive 10 x Steel Wrench into A1-S1-S3", not the raw body. */
	description: string;
	queuedAt: string;
	status: 'pending' | 'conflict';
	conflictMessage?: string;
};

const STORAGE_KEY = 'inventria.offlineQueue';

function load(): QueuedMovement[] {
	if (typeof localStorage === 'undefined') return [];

	try {
		const raw = localStorage.getItem(STORAGE_KEY);
		return raw ? JSON.parse(raw) : [];
	} catch {
		// A corrupted or hand-edited value is not worth crashing the app over -
		// treat it the same as no queue rather than let a JSON.parse failure
		// here take down every page that renders OfflineQueueBanner.
		return [];
	}
}

function persist() {
	if (typeof localStorage === 'undefined') return;
	localStorage.setItem(STORAGE_KEY, JSON.stringify(queue));
}

let queue = $state<QueuedMovement[]>(load());

export function queuedMovements(): QueuedMovement[] {
	return queue;
}

export function pendingCount(): number {
	return queue.filter((item) => item.status === 'pending').length;
}

export function conflictCount(): number {
	return queue.filter((item) => item.status === 'conflict').length;
}

function enqueue(
	endpoint: string,
	body: Record<string, unknown>,
	description: string
): QueuedMovement {
	const item: QueuedMovement = {
		id: crypto.randomUUID(),
		endpoint,
		body,
		description,
		queuedAt: new Date().toISOString(),
		status: 'pending'
	};
	queue = [...queue, item];
	persist();
	return item;
}

export function removeQueueItem(id: string) {
	queue = queue.filter((item) => item.id !== id);
	persist();
}

/**
 * What OfflineQueueBanner's "Retry" button calls for one conflicted item.
 * replayQueue on its own would not touch it - it only ever attempts items
 * still marked 'pending', which is what keeps it from silently re-trying a
 * conflict on every reconnect. Retrying is a person's explicit decision, made
 * once, not an automatic loop.
 */
export async function retryQueueItem(id: string): Promise<void> {
	queue = queue.map((item) =>
		item.id === id ? { ...item, status: 'pending' as const, conflictMessage: undefined } : item
	);
	persist();
	await replayQueue();
}

/**
 * The three stock-movement forms call this instead of apiFetch directly.
 * `fetch` throwing (a TypeError, never an HTTP status) means the request
 * never reached the server at all - the definition of "in a dead zone" this
 * whole feature exists for - so that, and only that, is what gets queued
 * rather than shown as a failure. A 401, a 400, a 409 all resolve normally
 * with a Response and are handled exactly as before this feature existed.
 */
export async function submitMovementOrQueue(
	endpoint: string,
	body: Record<string, unknown>,
	description: string
): Promise<
	| { outcome: 'submitted'; data: { message: string } }
	| { outcome: 'queued' }
	| { outcome: 'expired' }
	| { outcome: 'rejected'; message: string }
> {
	try {
		const response = await apiFetch(endpoint, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify(body)
		});

		if (response.status === 401) {
			endExpiredSession();
			return { outcome: 'expired' };
		}

		if (!response.ok) {
			return {
				outcome: 'rejected',
				message: await apiErrorMessage(response, 'Failed to process transaction.')
			};
		}

		return { outcome: 'submitted', data: await response.json() };
	} catch (err) {
		if (err instanceof TypeError) {
			enqueue(endpoint, body, description);
			return { outcome: 'queued' };
		}
		throw err;
	}
}

let replaying = false;

/**
 * Attempts every 'pending' item in the order it was queued, oldest first -
 * the order stock actually changed hands. Stops at the first item that
 * cannot reach the server at all (still offline) and leaves the rest
 * pending for the next attempt.
 *
 * A pending item that DOES reach the server and comes back rejected -
 * insufficient stock, an archived item, or the RowVersion concurrency check
 * on InventoryBalance catching a conflicting change made while this was
 * queued - is marked 'conflict' and left in the queue rather than retried.
 * That is the whole point: a queued write replaying against state that has
 * since moved on is exactly the scenario RowVersion exists to catch, and
 * silently retrying it (or worse, silently dropping it) would either apply a
 * stale decision or lose one - both worse than making a person look at it.
 */
export async function replayQueue(): Promise<void> {
	if (replaying) return;
	replaying = true;

	try {
		for (const item of [...queue]) {
			if (item.status !== 'pending') continue;

			let response: Response;
			try {
				response = await apiFetch(item.endpoint, {
					method: 'POST',
					headers: { 'Content-Type': 'application/json' },
					body: JSON.stringify(item.body)
				});
			} catch (err) {
				if (err instanceof TypeError) return; // still offline - try again next time
				throw err;
			}

			if (response.status === 401) {
				endExpiredSession();
				return;
			}

			if (response.ok) {
				removeQueueItem(item.id);
				continue;
			}

			const message = await apiErrorMessage(
				response,
				'This movement was rejected when it was resent.'
			);
			queue = queue.map((q) =>
				q.id === item.id ? { ...q, status: 'conflict', conflictMessage: message } : q
			);
			persist();
		}
	} finally {
		replaying = false;
	}
}

/**
 * Wires replayQueue to actually run: once now (a queue built up while the
 * app was closed, then reopened already back in range) and again every time
 * the browser fires 'online' (walking back into coverage mid-session).
 * Called once from the root layout; the returned function undoes it.
 */
export function startAutoReplay(): () => void {
	if (typeof window === 'undefined') return () => {};

	if (navigator.onLine) void replayQueue();

	const handler = () => void replayQueue();
	window.addEventListener('online', handler);
	return () => window.removeEventListener('online', handler);
}
