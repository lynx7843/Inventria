<script lang="ts">
	// A multi-series line chart over evenly spaced points, with a clickable
	// legend and a crosshair tooltip. Plain SVG for the same reason as PieChart:
	// a few paths and some arithmetic do not need a library behind them.
	import { untrack, type Snippet } from 'svelte';

	export type LineSeries = { id: number | string; label: string; values: number[] };

	let {
		title,
		xLabels,
		series,
		unit = 'units',
		loading = false,
		error = '',
		emptyText = 'Nothing to show yet.',
		controls
	}: {
		title: string;
		/** One label per point, oldest first - every series has a value for each. */
		xLabels: string[];
		series: LineSeries[];
		unit?: string;
		loading?: boolean;
		error?: string;
		emptyText?: string;
		/** Rendered at the right of the title row, e.g. a range picker. */
		controls?: Snippet;
	} = $props();

	// A fixed hue order checked for colour-blind separation. A ninth line would
	// need a ninth hue, and an invented one is never distinguishable enough, so
	// at most eight lines are drawn at once and the legend chooses which.
	const COLORS = [
		'#2a78d6',
		'#eb6834',
		'#1baf7a',
		'#eda100',
		'#e87ba4',
		'#008300',
		'#6250d6',
		'#e34948'
	];

	// Which colour each visible line holds. A colour belongs to its item for as
	// long as the line is shown - hiding one line must never repaint the others,
	// or the legend a reader just learned stops matching the chart.
	let slots: Record<string, number> = $state({});

	$effect(() => {
		const ids = series.map((s) => String(s.id));
		const current = untrack(() => slots);
		const next: Record<string, number> = {};

		for (const id of ids) if (id in current) next[id] = current[id];

		// First load, or every remembered line is gone: show the first eight.
		if (Object.keys(next).length === 0) {
			ids.slice(0, COLORS.length).forEach((id, i) => (next[id] = i));
		}

		slots = next;
	});

	function freeSlot(): number | undefined {
		const taken = new Set(Object.values(slots));
		const slot = COLORS.findIndex((_, i) => !taken.has(i));
		return slot === -1 ? undefined : slot;
	}

	function toggle(id: number | string) {
		const key = String(id);
		if (key in slots) {
			const rest = { ...slots };
			delete rest[key];
			slots = rest;
			return;
		}
		const slot = freeSlot();
		if (slot !== undefined) slots = { ...slots, [key]: slot };
	}

	const shown = $derived(
		series
			.filter((s) => String(s.id) in slots)
			.map((s) => ({ ...s, color: COLORS[slots[String(s.id)]] }))
	);

	// --- Geometry ---------------------------------------------------------

	const HEIGHT = 240;
	const PAD = { top: 10, right: 12, bottom: 26, left: 44 };

	let width = $state(0);
	const plotW = $derived(Math.max(0, width - PAD.left - PAD.right));
	const plotH = HEIGHT - PAD.top - PAD.bottom;
	const count = $derived(xLabels.length);

	// The axis tops out at a round number just above the tallest visible line,
	// with a handful of gridlines on round steps between. Stock is counted in
	// whole units, so a step is never below 1 - a fractional one would print
	// as the same rounded label twice.
	const scale = $derived.by(() => {
		const max = Math.max(1, ...shown.flatMap((s) => s.values));
		const raw = Math.max(1, max / 4);
		const pow = 10 ** Math.floor(Math.log10(raw));
		const step = [1, 2, 5, 10].map((m) => m * pow).find((s) => s >= raw) ?? raw;
		const top = Math.ceil(max / step) * step;
		const ticks = [];
		for (let t = 0; t <= top; t += step) ticks.push(t);
		return { top, ticks };
	});

	function x(i: number) {
		return PAD.left + (count <= 1 ? plotW / 2 : (i * plotW) / (count - 1));
	}

	function y(v: number) {
		return PAD.top + plotH - (v / scale.top) * plotH;
	}

	function linePath(values: number[]) {
		return values.map((v, i) => `${i === 0 ? 'M' : 'L'}${x(i)},${y(v)}`).join('');
	}

	// About one date label per 90px, always including the first and last day.
	const xTicks = $derived.by(() => {
		if (count === 0) return [];
		const n = Math.min(count, Math.max(2, Math.floor(plotW / 90)));
		return [
			...new Set(Array.from({ length: n }, (_, k) => Math.round((k * (count - 1)) / (n - 1))))
		];
	});

	// --- Interaction ------------------------------------------------------

	let hoverIndex: number | null = $state(null);
	let highlighted: string | null = $state(null);

	function trackPointer(event: PointerEvent) {
		const rect = (event.currentTarget as SVGRectElement).getBoundingClientRect();
		const px = event.clientX - rect.left;
		const i = count <= 1 ? 0 : Math.round((px / rect.width) * (count - 1));
		hoverIndex = Math.min(count - 1, Math.max(0, i));
	}

	const tooltipRows = $derived.by(() => {
		const i = hoverIndex;
		return i === null ? [] : [...shown].sort((a, b) => b.values[i] - a.values[i]);
	});

	const summary = $derived(
		shown.map((s) => `${s.label} ${(s.values.at(-1) ?? 0).toLocaleString()} ${unit} now`).join(', ')
	);
</script>

<section class="line-panel">
	<div class="line-header">
		<h3>{title}</h3>
		{@render controls?.()}
	</div>

	{#if error}
		<p class="empty error">{error}</p>
	{:else if loading && series.length === 0}
		<p class="empty">Loading...</p>
	{:else if series.length === 0}
		<p class="empty">{emptyText}</p>
	{:else}
		<div class="plot" class:refreshing={loading} bind:clientWidth={width}>
			{#if width > 0}
				<svg {width} height={HEIGHT} role="img" aria-label="{title}: {summary}">
					{#each scale.ticks as tick (tick)}
						<line
							class="grid"
							class:baseline={tick === 0}
							x1={PAD.left}
							x2={PAD.left + plotW}
							y1={y(tick)}
							y2={y(tick)}
						/>
						<text class="y-label" x={PAD.left - 8} y={y(tick)} dy="0.32em"
							>{tick.toLocaleString()}</text
						>
					{/each}

					{#each xTicks as i (i)}
						<text
							class="x-label"
							x={x(i)}
							y={HEIGHT - 6}
							text-anchor={i === 0 ? 'start' : i === count - 1 ? 'end' : 'middle'}
							>{xLabels[i]}</text
						>
					{/each}

					{#each shown as line (line.id)}
						<path
							class="line"
							class:faded={highlighted !== null && highlighted !== String(line.id)}
							class:bold={highlighted === String(line.id)}
							d={linePath(line.values)}
							stroke={line.color}
						/>
					{/each}

					{#if hoverIndex !== null}
						<line
							class="crosshair"
							x1={x(hoverIndex)}
							x2={x(hoverIndex)}
							y1={PAD.top}
							y2={PAD.top + plotH}
						/>
						{#each shown as line (line.id)}
							<circle
								class="dot"
								cx={x(hoverIndex)}
								cy={y(line.values[hoverIndex])}
								r="4"
								fill={line.color}
							/>
						{/each}
					{/if}

					<!-- Covers the whole plot rather than just the 2px lines, so the
               tooltip follows the pointer anywhere over the chart. -->
					<rect
						class="hit"
						x={PAD.left}
						y={PAD.top}
						width={plotW}
						height={plotH}
						role="presentation"
						onpointermove={trackPointer}
						onpointerleave={() => (hoverIndex = null)}
					/>
				</svg>

				{#if hoverIndex !== null && tooltipRows.length > 0}
					<div
						class="tooltip"
						class:flip={x(hoverIndex) > width * 0.6}
						style="left: {x(hoverIndex)}px; top: {PAD.top}px"
					>
						<strong>{xLabels[hoverIndex]}</strong>
						{#each tooltipRows as row (row.id)}
							<span class="tip-row">
								<span class="swatch" style="background: {row.color}"></span>
								<span class="tip-label">{row.label}</span>
								<span class="tip-value">{row.values[hoverIndex].toLocaleString()}</span>
							</span>
						{/each}
					</div>
				{/if}
			{/if}
		</div>

		<!-- The legend is also the chart's text view: every line's name and where it
         stands now, readable without telling colours apart. -->
		<ul class="legend">
			{#each series as s (s.id)}
				{@const key = String(s.id)}
				{@const on = key in slots}
				{@const full = !on && freeSlot() === undefined}
				<li>
					<button
						type="button"
						class:off={!on}
						aria-pressed={on}
						disabled={full}
						title={full ? `Up to ${COLORS.length} lines at a time - hide one first` : undefined}
						onclick={() => toggle(s.id)}
						onpointerenter={() => (highlighted = on ? key : null)}
						onpointerleave={() => (highlighted = null)}
					>
						<span class="swatch" style={on ? `background: ${COLORS[slots[key]]}` : ''}></span>
						<span class="legend-label">{s.label}</span>
						<span class="legend-value">{(s.values.at(-1) ?? 0).toLocaleString()}</span>
					</button>
				</li>
			{/each}
		</ul>
	{/if}
</section>

<style>
	.line-panel {
		background: white;
		padding: 1.5rem;
		border-radius: 8px;
		border: 1px solid #e2e8f0;
		min-width: 0;
	}
	.line-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 1rem;
		margin-bottom: 1rem;
	}
	.line-header h3 {
		margin: 0;
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
	.empty.error {
		color: #b91c1c;
		font-style: normal;
	}

	.plot {
		position: relative;
		transition: opacity 0.15s;
	}
	.plot.refreshing {
		opacity: 0.5;
	}
	.plot svg {
		display: block;
		overflow: visible;
	}

	.grid {
		stroke: #f1f5f9;
		stroke-width: 1;
	}
	.grid.baseline {
		stroke: #cbd5e1;
	}
	.y-label,
	.x-label {
		fill: #64748b;
		font-size: 11px;
		font-variant-numeric: tabular-nums;
	}
	.y-label {
		text-anchor: end;
	}

	.line {
		fill: none;
		stroke-width: 2;
		stroke-linejoin: round;
		stroke-linecap: round;
		transition:
			opacity 0.15s,
			stroke-width 0.15s;
	}
	.line.faded {
		opacity: 0.2;
	}
	.line.bold {
		stroke-width: 3;
	}
	.crosshair {
		stroke: #94a3b8;
		stroke-width: 1;
		stroke-dasharray: 3 3;
		pointer-events: none;
	}
	/* The white ring keeps a dot readable where lines cross. */
	.dot {
		stroke: white;
		stroke-width: 2;
		pointer-events: none;
	}
	.hit {
		fill: transparent;
		cursor: crosshair;
	}

	.tooltip {
		position: absolute;
		transform: translateX(12px);
		pointer-events: none;
		background: #0f172a;
		color: white;
		padding: 0.5rem 0.65rem;
		border-radius: 6px;
		font-size: 0.8rem;
		white-space: nowrap;
		display: flex;
		flex-direction: column;
		gap: 0.2rem;
		z-index: 1;
	}
	.tooltip.flip {
		transform: translateX(calc(-100% - 12px));
	}
	.tip-row {
		display: grid;
		grid-template-columns: 10px 1fr auto;
		align-items: center;
		gap: 0.5rem;
	}
	.tip-label {
		color: #cbd5e1;
	}
	.tip-value {
		font-weight: 600;
		font-variant-numeric: tabular-nums;
		text-align: right;
	}

	.legend {
		list-style: none;
		margin: 1rem 0 0 0;
		padding: 0;
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(170px, 1fr));
		gap: 0.25rem 1rem;
	}
	.legend button {
		width: 100%;
		display: grid;
		grid-template-columns: 12px 1fr auto;
		align-items: center;
		gap: 0.5rem;
		padding: 0.3rem 0.25rem;
		background: none;
		border: none;
		border-radius: 4px;
		font: inherit;
		font-size: 0.85rem;
		text-align: left;
		cursor: pointer;
	}
	.legend button:hover:not(:disabled) {
		background: #f8fafc;
	}
	.legend button:disabled {
		cursor: not-allowed;
	}
	.legend button.off .legend-label,
	.legend button.off .legend-value {
		color: #94a3b8;
	}
	.swatch {
		width: 10px;
		height: 10px;
		border-radius: 3px;
	}
	.legend .swatch {
		width: 12px;
		height: 12px;
		box-sizing: border-box;
		border: 2px solid #cbd5e1;
	}
	.legend button:not(.off) .swatch {
		border: none;
	}
	.legend-label {
		color: #0f172a;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}
	.legend-value {
		color: #0f172a;
		font-weight: 600;
		font-variant-numeric: tabular-nums;
	}
</style>
