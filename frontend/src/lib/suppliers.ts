import { getJson } from '$lib/api';

/** A supplier, as `GET /api/suppliers` returns it. */
export type Supplier = {
	id: number;
	name: string;
	contactName: string | null;
	email: string | null;
	phone: string | null;
	address: string | null;
	leadTimeDays: number | null;
};

export function fetchSuppliers(): Promise<Supplier[]> {
	return getJson<Supplier[]>('/api/suppliers', 'suppliers');
}
