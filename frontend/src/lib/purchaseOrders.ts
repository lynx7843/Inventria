import { getJson } from '$lib/api';

/** Where a purchase order sits - mirrors PurchaseOrderStatus on the API. */
export type PurchaseOrderStatus =
	| 'Draft'
	| 'Ordered'
	| 'PartiallyReceived'
	| 'Received'
	| 'Cancelled';

export const PURCHASE_ORDER_STATUSES: PurchaseOrderStatus[] = [
	'Draft',
	'Ordered',
	'PartiallyReceived',
	'Received',
	'Cancelled'
];

/** One row of `GET /api/purchase-orders` - a summary, not the lines underneath it. */
export type PurchaseOrderSummary = {
	id: number;
	supplierId: number;
	supplierName: string | null;
	status: PurchaseOrderStatus;
	createdAt: string;
	orderedAt: string | null;
	receivedAt: string | null;
	expectedDate: string | null;
	lineCount: number;
	totalOrdered: number;
	totalReceived: number;
};

export type PurchaseOrderPage = {
	orders: PurchaseOrderSummary[];
	page: number;
	pageSize: number;
	totalCount: number;
	totalPages: number;
};

/** One line of a purchase order, as `GET /api/purchase-orders/{id}` returns it. */
export type PurchaseOrderLine = {
	id: number;
	itemId: number;
	itemName: string | null;
	itemSku: string | null;
	quantityOrdered: number;
	quantityReceived: number;
	quantityRemaining: number;
	unitCost: number | null;
};

/** The full order, as `GET /api/purchase-orders/{id}` (and every write below) returns it. */
export type PurchaseOrderDetail = {
	id: number;
	supplierId: number;
	supplierName: string | null;
	status: PurchaseOrderStatus;
	createdAt: string;
	createdBy: string;
	orderedAt: string | null;
	receivedAt: string | null;
	expectedDate: string | null;
	notes: string | null;
	lines: PurchaseOrderLine[];
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

export function fetchPurchaseOrders(opts: {
	status?: string;
	supplierId?: number;
	page?: number;
	pageSize?: number;
}): Promise<PurchaseOrderPage> {
	const { status, supplierId, page = 1, pageSize = 15 } = opts;
	return getJson<PurchaseOrderPage>(
		`/api/purchase-orders${query({ status, supplierId, page, pageSize })}`,
		'purchase orders'
	);
}

export function fetchPurchaseOrder(id: number): Promise<PurchaseOrderDetail> {
	return getJson<PurchaseOrderDetail>(`/api/purchase-orders/${id}`, 'the purchase order');
}

/** Whether a line still owes some of what it ordered - what a receive form is offered for. */
export function isLineOutstanding(line: PurchaseOrderLine): boolean {
	return line.quantityRemaining > 0;
}
