import { getJson } from '$lib/api';

/** One item's line, as `GET /api/dashboard/stock-history` returns it. */
export type StockHistorySeries = {
	itemId: number;
	sku: string;
	name: string;
	/** Units on hand at the close of each day in `dates`, the last one being right now. */
	levels: number[];
};

export type StockHistory = {
	/** Warehouse days, oldest first, as `yyyy-MM-dd`. */
	dates: string[];
	series: StockHistorySeries[];
};

/** The windows the chart offers. The API clamps anything else to 7-365 days. */
export const HISTORY_RANGES = [7, 30, 90] as const;

export function fetchStockHistory(days: number): Promise<StockHistory> {
	return getJson<StockHistory>(`/api/dashboard/stock-history?days=${days}`, 'stock history');
}
