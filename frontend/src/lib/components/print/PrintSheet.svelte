<script lang="ts">
	import type { Snippet } from 'svelte';
	import type { ResolvedPathname } from '$app/types';

	// The frame every printable page in the app sits in, and the one place the
	// print stylesheet lives.
	//
	// A printable page is a different thing from a screen: it has no sidebar, no
	// session header and no scrollbar, its width is fixed by the paper rather than
	// the window, and everything on it that exists to be clicked is wasted ink.
	// Rather than bolt a `@media print` block onto each of the app's dashboard
	// pages - where it would have to undo the fixed sidebar, the 250px content
	// offset, the panel backgrounds and the action buttons, once per page and
	// slightly differently each time - the pages meant for paper are their own
	// routes under /print and start from nothing.
	//
	// What that buys: no label printer and no driver. The barcode is inline SVG
	// (see Barcode.svelte), the layout is millimetres of ordinary CSS, and the
	// output is whatever the browser's own print dialog is pointed at - a laser
	// printer with a sheet of adhesive labels in the tray, or plain A4 to be cut
	// up, or Print to PDF.
	let {
		title,
		description = '',
		backHref,
		backLabel = 'Back',
		controls,
		children
	}: {
		/** Names the document - the on-screen heading, and the browser's print title. */
		title: string;
		description?: string;
		/**
		 * Typed as a resolved pathname rather than a plain string so callers have
		 * to hand this through `resolve()` - the same guarantee
		 * svelte/no-navigation-without-resolve enforces on a literal href, which
		 * it cannot check once the value arrives as a prop.
		 */
		backHref: ResolvedPathname;
		backLabel?: string;
		/**
		 * Anything that changes what gets printed - a filter, a copy count. Sits
		 * in the toolbar, so it disappears with the rest of the screen-only
		 * chrome rather than needing its own print rule on each sheet.
		 */
		controls?: Snippet;
		children: Snippet;
	} = $props();

	// Deliberately a button the operator presses rather than a print dialog that
	// opens itself on arrival. Two reasons: the barcodes are drawn by an effect
	// after this markup mounts, so a dialog fired on load can capture the page a
	// frame before the bars exist; and a sheet of labels is worth looking at
	// before it is worth committing a sheet of labels to.
	function print() {
		window.print();
	}
</script>

<svelte:head>
	<!-- Browsers put the document title in the printed page's header and offer it
	     as the filename when the target is Print to PDF, so it is worth being a
	     description of the sheet rather than of the app. -->
	<title>{title}</title>
</svelte:head>

<!-- Everything in here is screen-only: the controls for producing the paper are
     not part of the paper. -->
<div class="toolbar">
	<a class="back" href={backHref}>&larr; {backLabel}</a>
	<div class="toolbar-text">
		<h1>{title}</h1>
		{#if description}<p>{description}</p>{/if}
	</div>
	{#if controls}
		<div class="toolbar-controls">{@render controls()}</div>
	{/if}
	<button type="button" class="btn-solid" onclick={print}>Print</button>
</div>

<main class="sheet">
	{@render children()}
</main>

<style>
	/* No `size`. Fixing this to A4 would crop it on the Letter paper half the
	   world has loaded, and fixing it to Letter does the same in reverse - the
	   print dialog already knows what is in the tray. Margins are given in
	   millimetres because they are a physical distance, and 10mm clears the
	   unprintable edge on every consumer laser and inkjet worth naming. */
	@page {
		margin: 10mm;
	}

	.toolbar {
		display: flex;
		align-items: center;
		gap: 1.5rem;
		padding: 1.25rem 2rem;
		background: white;
		border-bottom: 1px solid #e2e8f0;
		position: sticky;
		top: 0;
		z-index: 1;
	}
	.toolbar-text {
		flex-grow: 1;
	}
	.toolbar h1 {
		margin: 0;
		font-size: 1.25rem;
		color: #0f172a;
	}
	.toolbar p {
		margin: 0.25rem 0 0 0;
		font-size: 0.85rem;
		color: #64748b;
	}
	.back {
		color: #0b6b36;
		text-decoration: none;
		font-weight: 600;
		font-size: 0.9rem;
		white-space: nowrap;
	}
	.back:hover {
		text-decoration: underline;
	}
	.toolbar-controls {
		display: flex;
		align-items: flex-end;
		gap: 1rem;
		flex-shrink: 0;
	}
	.btn-solid {
		background: #0b6b36;
		color: white;
		border: none;
		padding: 0.6rem 1.5rem;
		border-radius: 6px;
		font-weight: 600;
		cursor: pointer;
		flex-shrink: 0;
	}
	.btn-solid:hover {
		background: #095028;
	}

	/* On screen the sheet is shown roughly as it will print - a fixed column the
	   width of A4 inside its margins, on a grey desk - so that what is about to
	   come out of the printer is what is being looked at. */
	.sheet {
		/* A4 less nothing: the 10mm padding stands in for @page's margin, so the
		   190mm left inside it is exactly the column the printer will have. What
		   fits here fits there. */
		width: 210mm;
		margin: 2rem auto;
		padding: 10mm;
		box-sizing: border-box;
		background: white;
		box-shadow: 0 1px 3px rgb(15 23 42 / 12%);
		color: #000;
	}

	@media print {
		/* The toolbar, and the offline-queue banner the root layout puts above
		   every page in the app. Both are things to act on, which paper is not.
		   The banner is reached with :global because it belongs to another
		   component - the print stylesheet has to be able to speak about the
		   chrome it is switching off. */
		.toolbar,
		:global(.offline-banner) {
			display: none !important;
		}

		/* The desk, the drop shadow and the centring column are all screen
		   fictions standing in for the paper. On the paper, @page owns the
		   margins and the sheet is simply the page. */
		.sheet {
			width: auto;
			margin: 0;
			padding: 0;
			box-shadow: none;
			background: none;
		}

		:global(body) {
			background: white;
			/* Hairlines and small type are what a printed sheet is made of, and
			   the screen sizes here are tuned for a screen. */
			font-size: 10pt;
			color: #000;
		}
	}
</style>
