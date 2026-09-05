import { getJson } from '$lib/api';

/** One row of `GET /api/reports/stock-on-hand` - an item as it sits in one bin. */
export type StockOnHandRow = {
	itemId: number;
	sku: string;
	name: string;
	category: string;
	warehouseBinId: number;
	zone: string;
	aisle: string;
	shelf: string;
	quantity: number;
};

export type StockOnHandPage = {
	items: StockOnHandRow[];
	page: number;
	pageSize: number;
	totalCount: number;
	totalPages: number;
	/** Sum of `quantity` across every row the filters match, not just this page. */
	totalUnits: number;
};

/** One row of `GET /api/reports/movements` - a line from the audit trail. */
export type MovementRow = {
	id: number;
	itemId: number;
	itemName: string;
	sku: string | null;
	warehouseBinId: number | null;
	zone: string | null;
	aisle: string | null;
	shelf: string | null;
	transactionType: string;
	quantityChanged: number;
	timestamp: string;
	performedBy: string;
};

export type MovementPage = {
	items: MovementRow[];
	page: number;
	pageSize: number;
	totalCount: number;
	totalPages: number;
};

/** One row of `GET /api/reports/dead-stock` - stock that has stopped moving. */
export type DeadStockRow = {
	id: number;
	sku: string;
	name: string;
	category: string;
	quantityOnHand: number;
	/** Null for an item that has never had a single recorded movement. */
	lastMovementAt: string | null;
};

export type DeadStockPage = {
	items: DeadStockRow[];
	page: number;
	pageSize: number;
	totalCount: number;
	totalPages: number;
	days: number;
};

/** One row of `GET /api/reports/velocity` - how fast an item is turning over. */
export type VelocityRow = {
	id: number;
	sku: string;
	name: string;
	category: string;
	unitsIn: number;
	unitsOut: number;
	netChange: number;
};

export type VelocityPage = {
	items: VelocityRow[];
	page: number;
	pageSize: number;
	totalCount: number;
	totalPages: number;
	days: number;
};

/** One row of `GET /api/reports/reorder` - an item that needs buying. */
export type ReorderRow = {
	itemId: number;
	sku: string;
	name: string;
	category: string;
	quantityOnHand: number;
	reorderPoint: number;
	/** The lot size. Zero when nobody has recorded one for this item. */
	reorderQuantity: number;
	/** How far under the point the item is. Zero for one sitting exactly on it. */
	shortfall: number;
	/**
	 * How much to order. Zero only for an item on its point with no lot size
	 * recorded - genuinely low, with nothing in the data to say how much of it to
	 * buy. Those rows are listed but never become purchase order lines.
	 */
	suggestedOrderQuantity: number;
};

export type ReorderPage = {
	items: ReorderRow[];
	page: number;
	pageSize: number;
	totalCount: number;
	totalPages: number;
	/** Units to order across everything the filters match, not just this page. */
	totalUnitsToOrder: number;
	/** Rows carrying a quantity - the lines a purchase order can be written from. */
	orderableCount: number;
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

export function fetchStockOnHand(opts: {
	category?: string;
	zone?: string;
	page?: number;
	pageSize?: number;
}): Promise<StockOnHandPage> {
	const { category, zone, page = 1, pageSize = 15 } = opts;
	return getJson<StockOnHandPage>(
		`/api/reports/stock-on-hand${query({ category, zone, page, pageSize })}`,
		'stock on hand'
	);
}

export function fetchMovements(opts: {
	from?: string;
	to?: string;
	type?: string;
	itemId?: number;
	performedBy?: string;
	page?: number;
	pageSize?: number;
}): Promise<MovementPage> {
	const { from, to, type, itemId, performedBy, page = 1, pageSize = 15 } = opts;
	return getJson<MovementPage>(
		`/api/reports/movements${query({ from, to, type, itemId, performedBy, page, pageSize })}`,
		'movements'
	);
}

export function fetchDeadStock(opts: {
	days?: number;
	page?: number;
	pageSize?: number;
}): Promise<DeadStockPage> {
	const { days = 90, page = 1, pageSize = 15 } = opts;
	return getJson<DeadStockPage>(
		`/api/reports/dead-stock${query({ days, page, pageSize })}`,
		'dead stock'
	);
}

export function fetchVelocity(opts: {
	days?: number;
	page?: number;
	pageSize?: number;
}): Promise<VelocityPage> {
	const { days = 30, page = 1, pageSize = 15 } = opts;
	return getJson<VelocityPage>(
		`/api/reports/velocity${query({ days, page, pageSize })}`,
		'velocity'
	);
}

export function fetchReorder(opts: {
	category?: string;
	page?: number;
	pageSize?: number;
}): Promise<ReorderPage> {
	const { category, page = 1, pageSize = 15 } = opts;
	return getJson<ReorderPage>(
		`/api/reports/reorder${query({ category, page, pageSize })}`,
		'the reorder list'
	);
}
