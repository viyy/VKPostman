<script lang="ts">
  import { ChevronDown, Check } from '@lucide/svelte';

  interface Tpl { id?: number; name: string; }
  interface Props {
    value: number | undefined;
    templates: Tpl[];
    onchange: (id: number | undefined) => void;
    id?: string;
  }
  let { value, templates, onchange, id }: Props = $props();

  let open = $state(false);
  let query = $state('');
  let rootEl: HTMLDivElement | undefined = $state();
  let searchEl: HTMLInputElement | undefined = $state();

  const selectedName = $derived(
    value != null ? (templates.find((t) => t.id === value)?.name ?? '(deleted)') : '',
  );

  const filtered = $derived.by(() => {
    const q = query.trim().toLowerCase();
    return q ? templates.filter((t) => t.name.toLowerCase().includes(q)) : templates;
  });

  // Focus the search box when the popup opens.
  $effect(() => {
    if (open) searchEl?.focus();
  });

  function choose(tid: number | undefined) {
    onchange(tid);
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
    <span class:placeholder={value == null}>{value == null ? '— none —' : selectedName}</span>
    <ChevronDown size={16} />
  </button>

  {#if open}
    <div class="ts-pop">
      <input
        bind:this={searchEl}
        class="ts-search"
        type="text"
        placeholder="Search templates…"
        bind:value={query}
        onkeydown={(e) => {
          if (e.key === 'Escape') { open = false; }
          else if (e.key === 'Enter' && filtered.length > 0) { e.preventDefault(); choose(filtered[0].id); }
        }}
      />
      <ul class="ts-list" role="listbox">
        <li>
          <button type="button" class="ts-opt" onclick={() => choose(undefined)}>
            <span class="ts-check">{#if value == null}<Check size={14} />{/if}</span>
            <span class="muted">— none —</span>
          </button>
        </li>
        {#each filtered as t (t.id)}
          <li>
            <button type="button" class="ts-opt" onclick={() => choose(t.id)}>
              <span class="ts-check">{#if value === t.id}<Check size={14} />{/if}</span>
              <span>{t.name}</span>
            </button>
          </li>
        {/each}
        {#if filtered.length === 0}
          <li class="ts-empty muted">No templates match “{query}”.</li>
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
  .ts-list {
    list-style: none;
    margin: 0;
    padding: 0;
    max-height: 240px;
    overflow: auto;
  }
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
  .ts-check { width: 16px; display: inline-flex; color: var(--vk-blue); }
  .ts-empty { padding: 0.5rem; font-size: 0.85rem; }
</style>
