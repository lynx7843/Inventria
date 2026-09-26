<script lang="ts">
	import JsBarcode from 'jsbarcode';

	// Renders `value` as a Code 128 symbol, inline in the page's own SVG - no
	// image request, no canvas, and nothing for a printer driver to be involved
	// in. Which is the whole reason this is SVG and not a PNG: a raster barcode
	// is resampled on its way to the paper and the narrow bars alias into each
	// other, whereas vector bars land on whatever grid the printer actually has.
	//
	// See $lib/barcode.ts for what goes in these and why. This component knows
	// about barcodes, not about warehouses.
	let {
		value,
		// Bar and space sizes are in CSS pixels, which print as 1/96in - so the
		// default 2px module is a hair over 0.5mm on paper, comfortably above the
		// ~0.25mm where an ordinary handheld scanner starts to struggle, and
		// still narrow enough that a shelf address fits across a label.
		moduleWidth = 2,
		height = 40,
		// The human-readable line underneath. Off by default: the pages here
		// print it themselves, in their own type, usually larger and alongside
		// something more useful to a person than the token (a bin's address, an
		// item's name).
		displayValue = false,
		label = ''
	}: {
		value: string;
		moduleWidth?: number;
		height?: number;
		displayValue?: boolean;
		/** Screen-reader text. Defaults to reading the encoded value out. */
		label?: string;
	} = $props();

	let svgEl: SVGSVGElement | undefined = $state();

	// JsBarcode refuses to encode some inputs - an empty string most of all,
	// which is what an id that failed to load looks like by the time it reaches
	// here. Without the `valid` callback below it answers that by throwing a
	// bare string (not an Error), which would take down the whole sheet on the
	// way to telling nobody anything. With it, the SVG is simply left empty, and
	// this flag is what puts something legible in its place.
	let isValid = $state(true);

	$effect(() => {
		if (!svgEl) return;

		JsBarcode(svgEl, value, {
			format: 'CODE128',
			width: moduleWidth,
			height,
			displayValue,
			fontSize: 12,
			textMargin: 2,
			// The quiet zone - the blank run either side that tells a scanner
			// where the symbol starts - is specified in modules, not millimetres:
			// Code 128 wants at least ten of them. JsBarcode's own default is a
			// flat 10px, which at any module width above 1 is less than half of
			// that, so it is computed here instead. A label crowded right up to
			// its own edge reads as a bad print rather than as a missing margin.
			margin: 10 * moduleWidth,
			background: 'transparent',
			lineColor: '#000000',
			valid: (valid: boolean) => {
				isValid = valid;
			}
		});
	});
</script>

<!-- Width and height are left exactly as JsBarcode sets them. Stretching the
     SVG would scale the module width with it, which is the one dimension of a
     barcode that is not decorative - it is what the scanner measures. Sheets
     size these by choosing moduleWidth, never with CSS. -->
<svg
	bind:this={svgEl}
	class="barcode"
	class:invalid={!isValid}
	role="img"
	aria-label={label || `Barcode: ${value}`}
></svg>
{#if !isValid}
	<span class="barcode-error">{value ? `Cannot encode "${value}"` : 'No barcode value'}</span>
{/if}

<style>
	.barcode {
		display: block;
		/* Bars have to stay black on white however the page is themed or however
		   the browser has been told to save ink - a grey barcode is a barcode the
		   scanner reads as nothing at all. */
		print-color-adjust: exact;
		-webkit-print-color-adjust: exact;
	}
	.barcode.invalid {
		display: none;
	}
	.barcode-error {
		display: inline-block;
		font-family: monospace;
		font-size: 0.7rem;
		color: #991b1b;
	}
</style>
