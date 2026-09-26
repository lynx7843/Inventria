/**
 * What Inventria puts inside the barcodes it prints, and why those values and
 * not others.
 *
 * Everything here is Code 128, which is the format to reach for when the thing
 * being encoded is an internal identifier rather than a retail product: it takes
 * the full ASCII range, has no fixed length to pad to, and carries its own check
 * character that the scanner verifies before it ever hands the value over. The
 * retail formats (EAN-13, UPC) are the wrong tool - they encode a fixed count of
 * digits issued by a numbering authority, which is what a manufacturer's product
 * barcode is and what a shelf address is not.
 *
 * The values are deliberately short. A Code 128 symbol grows by eleven modules
 * per character, so every character costs roughly 6mm of paper at a module width
 * a warehouse scanner can read comfortably. `BIN-00012` and `B12` scan exactly
 * the same and one of them fits on a shelf label.
 *
 * Each kind of token carries a one-letter prefix so a scan says what it is
 * without anyone having to know where it was scanned from - a bin label read into
 * a field expecting a pick line is a mistake the prefix catches, whereas two bare
 * numbers are indistinguishable. Item/SKU barcodes are absent on purpose: a
 * product that has a barcode already has the manufacturer's own on the packaging,
 * and printing a second one over it invites the scanner to read whichever it saw
 * first.
 */

/** What each prefix means, for the scanner-side dispatch that reads these back. */
export const BARCODE_PREFIX = {
	/** A storage location - `WarehouseBin.Id`. */
	bin: 'B',
	/** A pick list as a whole - `PickList.Id`. What the sheet's header carries. */
	pickList: 'P',
	/** One line of a pick list - `PickListLine.Id`. */
	pickLine: 'L'
} as const;

/** The barcode on a bin's shelf label. */
export function binBarcode(binId: number): string {
	return `${BARCODE_PREFIX.bin}${binId}`;
}

/** The barcode at the top of a pick sheet, for pulling the list up on a terminal. */
export function pickListBarcode(pickListId: number): string {
	return `${BARCODE_PREFIX.pickList}${pickListId}`;
}

/**
 * The barcode on one line of a pick sheet.
 *
 * The line id alone is enough - `PickListLine.Id` is a primary key, unique across
 * every list - so the list id is left out rather than spent on paper width.
 */
export function pickLineBarcode(pickListLineId: number): string {
	return `${BARCODE_PREFIX.pickLine}${pickListLineId}`;
}

/**
 * The id inside a scanned token, or null when the token is not one of ours.
 *
 * Nothing in the app scans yet; this exists so the encoding above is written down
 * in both directions in one file instead of being re-derived - by eye, from a
 * printed label - the first time something does.
 */
export function parseBarcode(
	scanned: string
): { kind: keyof typeof BARCODE_PREFIX; id: number } | null {
	const token = scanned.trim().toUpperCase();

	for (const [kind, prefix] of Object.entries(BARCODE_PREFIX)) {
		if (!token.startsWith(prefix)) continue;

		// Only digits after the prefix: `B12` is a bin, `B12X` is not this scheme
		// and guessing at it would turn a misread into a confident wrong answer.
		const digits = token.slice(prefix.length);
		if (!/^\d+$/.test(digits)) continue;

		const id = Number(digits);
		if (id > 0) return { kind: kind as keyof typeof BARCODE_PREFIX, id };
	}

	return null;
}
