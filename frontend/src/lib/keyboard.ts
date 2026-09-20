import { tick } from 'svelte';

/**
 * Whether a keystroke landed in something that treats it as ordinary typing -
 * an input, a select, a textarea, or anything contenteditable. Every global
 * shortcut below checks this first: a picker keying a quantity into a field
 * must never have a digit hijacked into switching tabs instead of being
 * typed.
 */
export function isTypingInField(target: EventTarget | null): boolean {
	if (!(target instanceof HTMLElement)) return false;

	const tag = target.tagName;
	return tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA' || target.isContentEditable;
}

/**
 * Svelte action: focuses this element the moment it mounts, so a form
 * appearing - the page loading, a tab switching, a create/edit panel opening -
 * drops the cursor straight into its first field instead of waiting for a
 * click. Takes a boolean rather than always focusing so one field in a form
 * can be marked as the one to autofocus without every field fighting over it.
 *
 * Auto-focusing on load is usually the wrong call for a content page - it
 * disorients anyone using a screen reader who didn't ask for it - but this is
 * a task screen a trained operator opens specifically to type into
 * immediately, the same trade-off a point-of-sale terminal makes.
 */
export function focusOnMount(node: HTMLElement, enabled: boolean = true) {
	if (!enabled) return;

	node.focus();

	// SvelteKit resets focus after every client-side navigation (its router's
	// reset_focus, which runs on a tick after this action does and so would
	// otherwise win, sending focus to <body> instead). That reset explicitly
	// prefers a real `autofocus` attribute over its own fallback, so setting
	// one here - inert for the browser itself, since autofocus only does
	// anything for an element already in the document when the page loads -
	// is what makes the field landed on by navigating here (login, a sidebar
	// link) stick the same way the field landed on by clicking or pressing a
	// shortcut while already on the page does.
	node.setAttribute('autofocus', '');
}

/**
 * Moves focus to the element with this id, after waiting for whatever state
 * change was supposed to reveal or restore it. Svelte does not apply a state
 * change's effect on the DOM synchronously with the assignment that caused
 * it, so calling this right after e.g. `showForm = false` can run before the
 * button it is aiming for exists again - `tick()` is what closes that gap.
 */
export async function focusId(id: string) {
	await tick();
	document.getElementById(id)?.focus();
}
