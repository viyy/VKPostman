<script lang="ts">
  import { liveQuery } from 'dexie';
  import { db } from '../lib/db';
  import { nav } from '../lib/nav.svelte';
  import type { PostDraft, PostTemplate, TargetGroup } from '../lib/types';

  interface Props {
    onclose: () => void;
  }
  let { onclose }: Props = $props();

  const draftsQuery = liveQuery(() => db.drafts.orderBy('updatedAt').reverse().toArray());
  const templatesQuery = liveQuery(() => db.templates.orderBy('name').toArray());
  const groupsQuery = liveQuery(() => db.groups.orderBy('displayName').toArray());

  let drafts = $state<PostDraft[]>([]);
  let templates = $state<PostTemplate[]>([]);
  let groups = $state<TargetGroup[]>([]);

  $effect(() => { const s = draftsQuery.subscribe({ next: (v) => (drafts = v) }); return () => s.unsubscribe(); });
  $effect(() => { const s = templatesQuery.subscribe({ next: (v) => (templates = v) }); return () => s.unsubscribe(); });
  $effect(() => { const s = groupsQuery.subscribe({ next: (v) => (groups = v) }); return () => s.unsubscribe(); });

  let query = $state('');
  let inputEl: HTMLInputElement | undefined = $state();
  let droppedFile = $state(false);

  $effect(() => { inputEl?.focus(); });

  function onDrop(e: DragEvent) {
    e.preventDefault();
    const f = e.dataTransfer?.files?.[0];
    if (f) {
      query = f.name;
      droppedFile = true;
      inputEl?.focus();
    }
  }

  interface Result { type: 'draft' | 'template' | 'group'; id: number; title: string; subtitle: string; }

  const LIMIT = 8;

  const results = $derived.by<Result[]>(() => {
    const q = query.trim().toLowerCase();
    if (!q) return [];
    const out: Result[] = [];

    for (const d of drafts) {
      const hay = [
        d.title,
        d.commonText,
        d.notes ?? '',
        (d.themeTags ?? []).join(' '),
        (d.imageNotes ?? []).join(' '),
      ].join(' ').toLowerCase();
      if (hay.includes(q)) {
        const imgHit = (d.imageNotes ?? []).some((n) => n.toLowerCase().includes(q));
        out.push({
          type: 'draft',
          id: d.id!,
          title: d.title,
          subtitle: imgHit ? `🖼 image match · ${(d.imageNotes ?? []).join(', ')}` : 'draft',
        });
      }
      if (out.filter((r) => r.type === 'draft').length >= LIMIT) break;
    }

    for (const t of templates) {
      const hay = [t.name, t.description, t.bodyTemplate].join(' ').toLowerCase();
      if (hay.includes(q)) out.push({ type: 'template', id: t.id!, title: t.name, subtitle: t.description || 'template' });
      if (out.filter((r) => r.type === 'template').length >= LIMIT) break;
    }

    for (const g of groups) {
      const hay = [g.displayName, g.screenName, g.notes ?? ''].join(' ').toLowerCase();
      if (hay.includes(q)) out.push({ type: 'group', id: g.id!, title: g.displayName, subtitle: `@${g.screenName}` });
      if (out.filter((r) => r.type === 'group').length >= LIMIT) break;
    }

    return out;
  });

  const badge = { draft: '📝', template: '📄', group: '👥' } as const;

  function open(r: Result) {
    if (r.type === 'draft') nav.openDraft(r.id);
    else if (r.type === 'template') nav.openTemplate(r.id);
    else nav.openGroup(r.id);
    onclose();
  }

  function onInputKey(e: KeyboardEvent) {
    if (e.key === 'Enter' && results.length > 0) {
      e.preventDefault();
      open(results[0]);
    }
  }
</script>

<div class="overlay" role="presentation" onclick={onclose}>
  <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
  <div class="panel" role="dialog" aria-modal="true" tabindex="-1" aria-label="Global search" onclick={(e) => e.stopPropagation()}>
    <input
      bind:this={inputEl}
      class="search-box"
      type="text"
      placeholder="Search drafts, templates, groups… (or drop an image to find it by name)"
      bind:value={query}
      onkeydown={onInputKey}
      ondragover={(e) => e.preventDefault()}
      ondrop={onDrop}
    />
    {#if droppedFile}
      <p class="muted hint">Searching by dropped filename. Edit the box to change the query.</p>
    {/if}

    {#if query.trim() && results.length === 0}
      <p class="muted hint">No matches for “{query}”.</p>
    {:else if results.length > 0}
      <ul class="results">
        {#each results as r (r.type + r.id)}
          <li>
            <button class="result" onclick={() => open(r)}>
              <span class="r-badge">{badge[r.type]}</span>
              <span class="r-body">
                <span class="r-title">{r.title}</span>
                <span class="r-sub muted">{r.subtitle}</span>
              </span>
            </button>
          </li>
        {/each}
      </ul>
    {:else}
      <p class="muted hint">Type to search across everything. Press Esc to close, Enter to open the top hit.</p>
    {/if}
  </div>
</div>

<style>
  .overlay {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.45);
    display: flex;
    align-items: flex-start;
    justify-content: center;
    padding: 4rem 1rem;
    z-index: 90;
  }
  .panel {
    background: var(--vk-surface);
    border: 1px solid var(--vk-border);
    border-radius: var(--radius);
    box-shadow: var(--shadow-md);
    padding: 0.85rem;
    width: 100%;
    max-width: 620px;
  }
  .search-box { font-size: 1rem; }
  .hint { margin: 0.5rem 0 0; }
  .results { list-style: none; margin: 0.6rem 0 0; padding: 0; display: flex; flex-direction: column; gap: 2px; }
  .result {
    display: flex;
    align-items: center;
    gap: 0.6rem;
    width: 100%;
    text-align: left;
    appearance: none;
    border: none;
    background: transparent;
    font: inherit;
    color: inherit;
    padding: 0.45rem 0.5rem;
    border-radius: var(--radius-sm);
    cursor: pointer;
  }
  .result:hover { background: var(--vk-hover); }
  .r-badge { font-size: 1.05rem; }
  .r-body { display: flex; flex-direction: column; min-width: 0; }
  .r-title { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .r-sub { font-size: 0.78rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
</style>
