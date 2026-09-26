import { getJson } from '$lib/api';

/** Where a pick list sits - mirrors PickListStatus on the API. */
export type PickListStatus = 'Open' | 'Completed' | 'Cancelled';

export const PICK_LIST_STATUSES: PickListStatus[] = ['Open', 'Completed', 'Cancelled'];

/** One row of `GET /api/pick-lists` - a summary, not the lines underneath it. */
export type PickListSummary = {
	id: number;
	status: PickListStatus;
	createdAt: string;
	createdBy: string;
	completedAt: string | null;
	lineCount: number;
	/** Lines still owing something. Zero on a list whose every line is fulfilled. */
	linesRemaining: number;
};

export type PickListPage = {
	pickLists: PickListSummary[];
	page: number;
	pageSize: number;
	totalCount: number;
	totalPages: number;
};

/**
 * One line of a pick list, as `GET /api/pick-lists/{id}` returns it.
 *
 * The item and bin fields are nullable because the API reads them off navigation
 * properties it may not have loaded; in practice every line has both, and the
 * screens here fall back to the id rather than printing "null" on a pick sheet.
 */
export type PickListLine = {
	id: number;
	/** Where this line falls in the walking route. Assigned once, when the list opened. */
	sequence: number;
	itemId: number;
	itemName: string | null;
	itemSku: string | null;
	warehouseBinId: number;
	binZone: string | null;
	binAisle: string | null;
	binShelf: string | null;
	/** Set only for an item that tracks lots. */
	lotNumber: string | null;
	quantityRequested: number;
	quantityPicked: number;
	quantityRemaining: number;
};

/** The full list, as `GET /api/pick-lists/{id}` (and every write below) returns it. */
export type PickListDetail = {
	id: number;
	status: PickListStatus;
	createdAt: string;
	createdBy: string;
	completedAt: string | null;
	/** Already in `sequence` order - the API sorts them, so nothing here re-sorts. */
	lines: PickListLine[];
};

/** Builds a query string, leaving out anything unset rather than sending `?x=`. */
function query(params: Record<string, string | number | undefined>): string {
	const search = new URLSearchParams();
	for (const [key, value] of Object.entries(params)) {
		if (value !== undefined && value !== '') search.set(key, String(value));
	}
	const qs = search.toString();
	return qs ? `?${qs}` : '';
}

export function fetchPickLists(opts: {
	status?: string;
	page?: number;
	pageSize?: number;
}): Promise<PickListPage> {
	const { status, page = 1, pageSize = 15 } = opts;
	return getJson<PickListPage>(`/api/pick-lists${query({ status, page, pageSize })}`, 'pick lists');
}

export function fetchPickList(id: number): Promise<PickListDetail> {
	return getJson<PickListDetail>(`/api/pick-lists/${id}`, 'the pick list');
}

/** Whether a line still owes some of what it asked for - what a pick form is offered for. */
export function isLineOutstanding(line: PickListLine): boolean {
	return line.quantityRemaining > 0;
}

/**
 * The bin address on a pick list line, written the way `binLabel` writes a bin's.
 *
 * Separate from `binLabel` rather than reusing it because a line carries the
 * address flattened into its own nullable fields, not a `WarehouseBin`. A line
 * missing them falls back to the bin's id, which is at least something a person
 * can look up - `--` on a pick sheet sends a picker nowhere.
 */
export function lineBinLabel(line: PickListLine): string {
	const { binZone, binAisle, binShelf } = line;
	if (binZone && binAisle && binShelf) return `${binZone}-${binAisle}-${binShelf}`;
	return `Bin #${line.warehouseBinId}`;
}
