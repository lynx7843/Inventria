<script lang="ts">
	// A dropdown shaped like InputField, for the fields where the valid values are
	// rows in the database rather than anything a person should be typing.
	//
	// `options` is a list of { value, label }; `placeholder` is the disabled first
	// entry, and `emptyLabel` replaces it when there is nothing to choose from -
	// an empty dropdown otherwise looks broken rather than unpopulated.
	// The option shape is spelled out here rather than imported from $lib/inventory:
	// this component knows about dropdowns, not about warehouses, and the two
	// shapes are structurally identical so an Option[] still fits.
	type Choice = { value: number | string; label: string };

	let {
		id,
		label,
		options = [],
		value = $bindable(),
		placeholder = 'Select...',
		emptyLabel = 'None available',
		required = false,
		disabled = false,
		autofocus = false
	}: {
		id: string;
		label: string;
		options?: Choice[];
		value: string;
		placeholder?: string;
		emptyLabel?: string;
		required?: boolean;
		disabled?: boolean;
		// See InputField / focusOnMount - but this element can't act on it the
		// same way, so it isn't passed to that action here. See the $effect
		// below instead.
		autofocus?: boolean;
	} = $props();

	let selectEl: HTMLSelectElement | undefined = $state();
	let hasAutofocused = false;

	// A disabled element refuses .focus() outright, and this select starts
	// disabled - options arrive from an API call that has not resolved yet at
	// mount, which is exactly when a plain mount-time action would try and
	// silently fail. Effect rather than action so it can wait for options to
	// actually exist and try again once they do; `hasAutofocused` keeps it to
	// one attempt so a later, unrelated options refresh can't steal focus back
	// from wherever the person has since moved to.
	$effect(() => {
		if (autofocus && !hasAutofocused && options.length > 0 && selectEl) {
			hasAutofocused = true;
			selectEl.focus();
			// See InputField's focusOnMount for why: SvelteKit's post-navigation
			// focus reset prefers a real `autofocus` attribute over its own
			// fallback to <body>.
			selectEl.setAttribute('autofocus', '');
		}
	});
</script>

<div class="input-group">
	<label for={id}>{label}</label>
	<select
		{id}
		bind:value
		bind:this={selectEl}
		{required}
		disabled={disabled || options.length === 0}
	>
		<option value="" disabled selected>
			{options.length === 0 ? emptyLabel : placeholder}
		</option>
		{#each options as option (option.value)}
			<option value={String(option.value)}>{option.label}</option>
		{/each}
	</select>
</div>

<style>
	.input-group {
		margin-bottom: 1.5rem;
	}
	label {
		display: block;
		font-size: 0.75rem;
		font-weight: 600;
		color: #475569;
		margin-bottom: 0.5rem;
		letter-spacing: 0.5px;
	}
	select {
		width: 100%;
		padding: 0.75rem;
		border: 1px solid #cbd5e1;
		border-radius: 6px;
		box-sizing: border-box;
		outline: none;
		background: white;
		font-family: inherit;
		transition: border 0.2s;
	}
	select:focus {
		border-color: #0b6b36;
	}
	select:disabled {
		background: #f1f5f9;
		color: #94a3b8;
		cursor: not-allowed;
	}
</style>
