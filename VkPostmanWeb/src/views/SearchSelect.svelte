<script lang="ts">
  import { ChevronDown, Check } from '@lucide/svelte';

  export interface SearchItem { id?: number; label: string; sub?: string; }

  interface Props {
    value: number | undefined;
    items: SearchItem[];
    onchange: (id: number | undefined) => void;
    id?: string;
    /** Text on the closed trigger when nothing is selected. */
    triggerPlaceholder?: string;
    /** Placeholder inside the search box. */
    searchPlaceholder?: string;
    /** Show the "— none —" option (clears the value). */
    allowNone?: boolean;
    noneLabel?: string;
  }
  let {
    value,
    items,
    onchange,
    id,
    triggerPlaceholder = '— none —',
    searchPlaceholder = 'Search…',
    allowNone = true,
    noneLabel = '— none —',
  }: Props = $props();

  let open = $state(false);
  let query = $state('');
  let rootEl: HTMLDivElement | undefined = $state();
  let searchEl: HTMLInputElement | undefined = $state();

  const selectedLabel = $derived(
    value != null ? (items.find((i) => i.id === value)?.label ?? '(deleted)') : '',
  );

  const filtered = $derived.by(() => {
    const q = query.trim().toLowerCase();
    return q
      ? items.filter((i) => i.label.toLowerCase().includes(q) || (i.sub ?? '').toLowerCase().includes(q))
      : items;
  });

  $effect(() => {
    if (open) searchEl?.focus();
  });

  function choose(itemId: number | undefined) {
    onchange(itemId);
    open = false;
    query = '';
  }

  function onWindowClick(e: MouseEvent) {
    if (open && rootEl && !rootEl.contains(e.target as Node)) open = false;
  }
</script>

<div class="ts" bind:this={rootEl}>
  <button
    type="button"
    {id}
    class="ts-trigger"
    aria-haspopup="listbox"
    aria-expanded={open}
    onclick={() => { open = !open; query = ''; }}
  >
    <span class:placeholder={value == null}>{value == null ? triggerPlaceholder : selectedLabel}</span>
    <ChevronDown size={16} />
  </button>

  {#if open}
    <div class="ts-pop">
      <input
        bind:this={searchEl}
        class="ts-search"
        type="text"
        placeholder={searchPlaceholder}
        bind:value={query}
        onkeydown={(e) => {
          if (e.key === 'Escape') { open = false; }
          else if (e.key === 'Enter' && filtered.length > 0) { e.preventDefault(); choose(filtered[0].id); }
        }}
      />
      <ul class="ts-list" role="listbox">
        {#if allowNone}
          <li>
            <button type="button" class="ts-opt" onclick={() => choose(undefined)}>
              <span class="ts-check">{#if value == null}<Check size={14} />{/if}</span>
              <span class="muted">{noneLabel}</span>
            </button>
          </li>
        {/if}
        {#each filtered as it (it.id)}
          <li>
            <button type="button" class="ts-opt" onclick={() => choose(it.id)}>
              <span class="ts-check">{#if value === it.id}<Check size={14} />{/if}</span>
              <span class="ts-label">{it.label}</span>
              {#if it.sub}<span class="muted ts-sub">{it.sub}</span>{/if}
            </button>
          </li>
        {/each}
        {#if filtered.length === 0}
          <li class="ts-empty muted">No matches{query ? ` for “${query}”` : ''}.</li>
        {/if}
      </ul>
    </div>
  {/if}
</div>

<svelte:window onclick={onWindowClick} />

<style>
  .ts { position: relative; }
  .ts-trigger {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.5rem;
    width: 100%;
    padding: 8px 10px;
    font: inherit;
    color: inherit;
    text-align: left;
    background: var(--vk-surface);
    border: 1px solid var(--vk-border-strong);
    border-radius: var(--radius-sm);
    cursor: pointer;
  }
  .ts-trigger .placeholder { color: var(--vk-text-secondary); }
  .ts-pop {
    position: absolute;
    z-index: 30;
    top: calc(100% + 4px);
    left: 0;
    right: 0;
    background: var(--vk-surface);
    border: 1px solid var(--vk-border);
    border-radius: var(--radius-sm);
    box-shadow: var(--shadow-md);
    padding: 4px;
  }
  .ts-search { margin-bottom: 4px; }
  .ts-list { list-style: none; margin: 0; padding: 0; max-height: 240px; overflow: auto; }
  .ts-opt {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    width: 100%;
    text-align: left;
    appearance: none;
    border: none;
    background: transparent;
    font: inherit;
    color: inherit;
    padding: 0.4rem 0.5rem;
    border-radius: 4px;
    cursor: pointer;
  }
  .ts-opt:hover { background: var(--vk-hover); }
  .ts-check { width: 16px; flex-shrink: 0; display: inline-flex; color: var(--vk-blue); }
  .ts-label { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .ts-sub { margin-left: auto; font-size: 0.78rem; }
  .ts-empty { padding: 0.5rem; font-size: 0.85rem; }
</style>
