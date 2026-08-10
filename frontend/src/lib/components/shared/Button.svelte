<script lang="ts">
  import type { HTMLButtonAttributes } from 'svelte/elements';

  // `type` is not a free-form string to the DOM - it is one of three words, and
  // typing it as string was the last thing standing between this project and a
  // clean `npm run check`. Borrowing the attribute's own type means a typo like
  // type="sumbit" is a compile error rather than a button that silently stops
  // submitting the form it sits in.
  // The busy label was hardcoded to "AUTHENTICATING...", which is true of
  // exactly one button in the app - the one on the login form. Every other use
  // of this component told the user it was authenticating while it received a
  // pallet or moved stock between bins. It is a prop now, with a default that is
  // at least true of whatever is happening.
  let {
    text,
    type = "button",
    isLoading = false,
    loadingText = "WORKING..."
  }: {
    text: string;
    type?: HTMLButtonAttributes['type'];
    isLoading?: boolean;
    loadingText?: string;
  } = $props();
</script>

<button {type} class="btn-solid" disabled={isLoading}>
  {isLoading ? loadingText : text}
</button>

<style>
  .btn-solid { width: 100%; padding: 1rem; background: #0b6b36; color: white; border: none; border-radius: 6px; font-weight: 600; cursor: pointer; transition: background 0.2s; }
  .btn-solid:hover:not(:disabled) { background: #095028; }
  .btn-solid:disabled { background: #94a3b8; cursor: not-allowed; }
</style>