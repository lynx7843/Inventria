import { apiFetch, apiErrorMessage } from '$lib/api';
import { endExpiredSession } from '$lib/auth';

/** A master item, as `GET /api/inventory` returns it. */
export type Item = {
	id: number;
	sku: string;
	name: string;
	category: string;
	/** Units on the shelves for this item, summed across every bin holding it. */
	quantityOnHand: number;
};

/** A storage location, as `GET /api/warehousebins` returns it. */
export type WarehouseBin = {
	id: number;
	zone: string;
	aisle: string;
	shelf: string;
};

/** One entry in a `SelectField` dropdown. */
export type Option = {
	value: number;
	label: string;
};

/**
 * The address a picker walks to, written the way the API writes it in its
 * confirmation messages so the two read as the same place.
 */
export function binLabel(bin: WarehouseBin): string {
	return `${bin.zone}-${bin.aisle}-${bin.shelf}`;
}

/** SKU first: it is what people scan and search by, the name is the reminder. */
export function itemLabel(item: Item): string {
	return `${item.sku} — ${item.name}`;
}

/**
 * The number of units a stock move should send, or null when the box does not
 * hold one.
 *
 * `Number()` answers every wrong input with something that looks like a number
 * and is not: an empty box and a null both come back as 0, letters come back as
 * NaN, and "2.5" comes back as a quantity of goods that cannot exist. Sent on,
 * those became "Item with ID 0 not found", a JSON conversion failure quoting a
 * byte offset, or - for NaN, which JSON has no way to write - a null the API
 * reads as a missing field. None of them mention the quantity box, which is
 * where the mistake actually is.
 */
export function parseUnits(value: unknown): number | null {
	const units = Number(value);
	return Number.isInteger(units) && units > 0 ? units : null;
}

async function getJson<T>(path: string, what: string): Promise<T> {
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

/** One page of items, as `GET /api/inventory` returns it. */
export type ItemPage = {
	items: Item[];
	page: number;
	pageSize: number;
	totalCount: number;
	totalPages: number;
};

/** The page size the API uses when asked for the largest page it will serve. */
const MAX_PAGE_SIZE = 200;

/**
 * A single page of items, for the screens that show a table someone reads.
 */
export function fetchItemPage(page = 1, pageSize = 25): Promise<ItemPage> {
	return getJson<ItemPage>(`/api/inventory?page=${page}&pageSize=${pageSize}`, 'items');
}

/**
 * Every item, collected a page at a time.
 *
 * The dropdowns on the stock forms have to offer the whole catalogue - an item
 * missing from the list is an item nobody can receive - so paging the wire
 * cannot mean paging what they show. Requests run one after another because
 * each one's answer says whether there is another page to ask for.
 *
 * The cap is a guard against a runaway loop, not a supported catalogue size: a
 * warehouse with more items than this has outgrown picking from a dropdown and
 * wants a searchable field, which is a bigger change than this one. If it is
 * ever hit, the console says so rather than the list quietly ending early.
 */
export async function fetchAllItems(): Promise<Item[]> {
	const maxRequests = 25;
	const collected: Item[] = [];

	for (let page = 1; page <= maxRequests; page++) {
		const result = await fetchItemPage(page, MAX_PAGE_SIZE);
		collected.push(...result.items);

		if (page >= result.totalPages) return collected;
	}

	console.warn(
		`Stopped after ${maxRequests} pages of items; the pickers are showing the first ${collected.length}.`
	);

	return collected;
}

export function fetchBins(): Promise<WarehouseBin[]> {
	return getJson<WarehouseBin[]>('/api/warehousebins', 'warehouse bins');
}
