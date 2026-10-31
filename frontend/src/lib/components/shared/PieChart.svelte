<script lang="ts">
	// A part-to-whole pie with a legend beside it. Plain SVG rather than a chart
	// library: one pie is a handful of arcs, and a dependency for that would
	// outweigh the page it sits on.

	export type Slice = { label: string; value: number };

	let {
		title,
		slices,
		unit = 'units',
		loading = false,
		emptyText = 'Nothing to show yet.'
	}: {
		title: string;
		slices: Slice[];
		unit?: string;
		loading?: boolean;
		emptyText?: string;
	} = $props();

	// Past six slices a pie stops being readable at a glance, so the tail folds
	// into "Other". The hues are a fixed order checked for colour-blind
	// separation - a seventh is never invented, and "Other" is a neutral grey so
	// it never reads as one more category.
	const MAX_SLICES = 6;
	const COLORS = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#008300'];
	const OTHER_COLOR = '#94a3b8';

	const SIZE = 200;
	const CENTER = SIZE / 2;
	const RADIUS = SIZE / 2 - 2;

	type Segment = Slice & { color: string; share: number; path: string };

	const segments: Segment[] = $derived.by(() => {
		const positive = slices.filter((s) => s.value > 0).sort((a, b) => b.value - a.value);

		const shown =
			positive.length > MAX_SLICES
				? [
						...positive.slice(0, MAX_SLICES - 1),
						{
							label: 'Other',
							value: positive.slice(MAX_SLICES - 1).reduce((sum, s) => sum + s.value, 0)
						}
					]
				: positive;

		const total = shown.reduce((sum, s) => sum + s.value, 0);
		let angle = -Math.PI / 2; // start at twelve o'clock, go clockwise

		return shown.map((slice, i) => {
			const share = slice.value / total;
			const end = angle + share * 2 * Math.PI;
			const path = arcPath(angle, end);
			angle = end;

			const isOther = positive.length > MAX_SLICES && i === shown.length - 1;
			return { ...slice, share, path, color: isOther ? OTHER_COLOR : COLORS[i] };
		});
	});

	const total = $derived(segments.reduce((sum, s) => sum + s.value, 0));

	function point(angle: number) {
		return `${CENTER + RADIUS * Math.cos(angle)} ${CENTER + RADIUS * Math.sin(angle)}`;
	}

	// A wedge from the centre out to the rim and back. A single slice holding
	// everything is drawn as a circle instead, because an arc whose start and end
	// are the same point draws nothing at all.
	function arcPath(start: number, end: number) {
		const largeArc = end - start > Math.PI ? 1 : 0;
		return `M ${CENTER} ${CENTER} L ${point(start)} A ${RADIUS} ${RADIUS} 0 ${largeArc} 1 ${point(end)} Z`;
	}

	function percent(share: number) {
		if (share > 0 && share < 0.01) return '<1%';
		return `${Math.round(share * 100)}%`;
	}

	// Hovering a wedge or its legend row highlights the pair, and the wedge also
	// gets a tooltip at the pointer.
	let hovered: number | null = $state(null);
	let tip = $state({ x: 0, y: 0 });
	let chartEl: HTMLDivElement | undefined = $state();

	function trackPointer(event: PointerEvent, index: number) {
		hovered = index;
		if (!chartEl) return;
		const rect = chartEl.getBoundingClientRect();
		tip = { x: event.clientX - rect.left, y: event.clientY - rect.top };
	}

	const summary = $derived(
		segments.map((s) => `${s.label} ${s.value.toLocaleString()} ${unit}`).join(', ')
	);
</script>

<section class="pie-panel">
	<h3>{title}</h3>

	{#if loading}
		<p class="empty">Loading...</p>
	{:else if segments.length === 0}
		<p class="empty">{emptyText}</p>
	{:else}
		<div class="pie-body">
			<div class="pie" bind:this={chartEl}>
				<svg viewBox="0 0 {SIZE} {SIZE}" role="img" aria-label="{title}: {summary}">
					{#each segments as segment, i (segment.label)}
						{#if segments.length === 1}
							<circle
								cx={CENTER}
								cy={CENTER}
								r={RADIUS}
								fill={segment.color}
								role="presentation"
								onpointermove={(e) => trackPointer(e, i)}
								onpointerleave={() => (hovered = null)}
							/>
						{:else}
							<path
								d={segment.path}
								fill={segment.color}
								class:dimmed={hovered !== null && hovered !== i}
								role="presentation"
								onpointermove={(e) => trackPointer(e, i)}
								onpointerleave={() => (hovered = null)}
							/>
						{/if}
					{/each}
				</svg>

				{#if hovered !== null && segments[hovered]}
					<div class="tooltip" style="left: {tip.x}px; top: {tip.y}px">
						<strong>{segments[hovered].label}</strong>
						<span
							>{segments[hovered].value.toLocaleString()}
							{unit} · {percent(segments[hovered].share)}</span
						>
					</div>
				{/if}
			</div>

			<ul class="legend">
				{#each segments as segment, i (segment.label)}
					<li
						class:dimmed={hovered !== null && hovered !== i}
						onpointerenter={() => (hovered = i)}
						onpointerleave={() => (hovered = null)}
					>
						<span class="swatch" style="background: {segment.color}"></span>
						<span class="label">{segment.label}</span>
						<span class="value">{segment.value.toLocaleString()}</span>
						<span class="share">{percent(segment.share)}</span>
					</li>
				{/each}
				<li class="total">
					<span class="swatch"></span>
					<span class="label">Total</span>
					<span class="value">{total.toLocaleString()}</span>
					<span class="share">{unit}</span>
				</li>
			</ul>
		</div>
	{/if}
</section>

<style>
	.pie-panel {
		background: white;
		padding: 1.5rem;
		border-radius: 8px;
		border: 1px solid #e2e8f0;
		min-width: 0;
	}
	.pie-panel h3 {
		margin: 0 0 1.5rem 0;
		font-size: 1.1rem;
		color: #0f172a;
	}
	.empty {
		margin: 0;
		padding: 2rem 0;
		text-align: center;
		color: #64748b;
		font-style: italic;
	}

	.pie-body {
		display: flex;
		align-items: center;
		gap: 2rem;
		flex-wrap: wrap;
	}
	.pie {
		position: relative;
		flex: 0 0 auto;
		width: 200px;
		max-width: 100%;
	}
	.pie svg {
		display: block;
		width: 100%;
		height: auto;
	}
	/* The white stroke is the gap between wedges, so neighbouring colours never
	   touch and each wedge reads as its own shape. */
	.pie path,
	.pie circle {
		stroke: white;
		stroke-width: 2;
		stroke-linejoin: round;
		cursor: default;
		transition: opacity 0.15s;
	}
	.dimmed {
		opacity: 0.35;
	}

	.tooltip {
		position: absolute;
		transform: translate(12px, -50%);
		pointer-events: none;
		background: #0f172a;
		color: white;
		padding: 0.4rem 0.6rem;
		border-radius: 6px;
		font-size: 0.8rem;
		white-space: nowrap;
		display: flex;
		flex-direction: column;
		gap: 0.1rem;
		z-index: 1;
	}
	.tooltip span {
		color: #cbd5e1;
	}

	.legend {
		flex: 1 1 180px;
		list-style: none;
		margin: 0;
		padding: 0;
		font-size: 0.9rem;
	}
	.legend li {
		display: grid;
		grid-template-columns: 12px 1fr auto 3.5rem;
		align-items: center;
		gap: 0.6rem;
		padding: 0.4rem 0;
		border-bottom: 1px solid #f1f5f9;
		transition: opacity 0.15s;
	}
	.swatch {
		width: 12px;
		height: 12px;
		border-radius: 3px;
	}
	.label {
		color: #0f172a;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}
	.value {
		color: #0f172a;
		font-weight: 600;
		font-variant-numeric: tabular-nums;
	}
	.share {
		color: #64748b;
		text-align: right;
		font-variant-numeric: tabular-nums;
	}
	.legend .total {
		border-bottom: none;
		font-weight: 600;
	}
	.legend .total .label {
		color: #64748b;
	}
</style>
