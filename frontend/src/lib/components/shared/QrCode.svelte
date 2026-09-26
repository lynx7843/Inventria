<script lang="ts">
	import QRCode from 'qrcode';

	// Renders `value` as a QR code, inline in the page's own SVG - the same
	// reasoning as Barcode.svelte, and here it is not only about print quality.
	// What this draws during enrolment is the account's TOTP secret, and the
	// usual way to put a QR on a page is an <img> pointed at a chart service,
	// which would hand that secret to a third party in a URL that then sits in
	// their logs. Nothing leaves the browser.
	let {
		value,
		size = 180,
		label = 'QR code'
	}: {
		value: string;
		/** Rendered edge length in CSS pixels. */
		size?: number;
		label?: string;
	} = $props();

	let markup = $state('');
	let failed = $state('');

	$effect(() => {
		// toString is async, so this can resolve after `value` has already
		// changed again. The flag is what stops an earlier, slower render
		// overwriting a later one.
		let current = true;

		QRCode.toString(value, {
			type: 'svg',
			// Error correction eats capacity to survive damage. An otpauth URI
			// is short and this is scanned off a clean screen from 30cm away,
			// so the lowest level keeps the modules large - which is what
			// actually helps a phone camera here.
			errorCorrectionLevel: 'L',
			margin: 2,
			color: { dark: '#000000', light: '#ffffff' }
		})
			.then((svg) => {
				if (current) markup = svg;
			})
			.catch((err) => {
				console.error(err);
				if (current) failed = 'Could not draw the QR code - type the key in instead.';
			});

		return () => {
			current = false;
		};
	});
</script>

<!-- The library returns a complete <svg> element with its own viewBox, so the
     wrapper sizes it and the SVG scales to fit. Unlike a barcode there is no
     minimum module width to preserve: a QR is read as a grid, and a phone
     either resolves the squares or moves closer. -->
<div class="qr" style="width: {size}px; height: {size}px" role="img" aria-label={label}>
	{#if failed}
		<span class="qr-error">{failed}</span>
	{:else}
		<!-- eslint-disable-next-line svelte/no-at-html-tags -->
		{@html markup}
	{/if}
</div>

<style>
	.qr {
		display: flex;
		align-items: center;
		justify-content: center;
		background: white;
		border: 1px solid #e2e8f0;
		border-radius: 8px;
		padding: 0;
		box-sizing: content-box;
		flex-shrink: 0;
	}
	.qr :global(svg) {
		width: 100%;
		height: 100%;
		display: block;
	}
	.qr-error {
		font-size: 0.75rem;
		color: #b91c1c;
		text-align: center;
		padding: 0.5rem;
	}
</style>
