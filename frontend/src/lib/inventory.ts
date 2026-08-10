import { apiFetch } from '$lib/api';

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

async function getJson<T>(path: string, what: string): Promise<T> {
	const res = await apiFetch(path);

	if (res.status === 401) {
		// The session is gone; the page guards send the visitor back to login.
		window.location.href = '/';
		throw new Error('Your session has expired.');
	}

	if (!res.ok) throw new Error(`Failed to load ${what}.`);

	return res.json();
}

export function fetchItems(): Promise<Item[]> {
	return getJson<Item[]>('/api/inventory', 'items');
}

export function fetchBins(): Promise<WarehouseBin[]> {
	return getJson<WarehouseBin[]>('/api/warehousebins', 'warehouse bins');
}
