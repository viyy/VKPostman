<script lang="ts">
  import { liveQuery } from 'dexie';
  import { db, createGroup, saveGroup, deleteGroup } from '../lib/db';
  import type { TargetGroup } from '../lib/types';
  import { createAutosave, type AutosaveStatus } from '../lib/autosave';
  import { knownTagsQuery } from '../lib/tags';
  import { undo } from '../lib/undo.svelte';
  import TagSuggestions from './TagSuggestions.svelte';
  import SearchSelect from './SearchSelect.svelte';
  import { nav } from '../lib/nav.svelte';
  import { exportSubset, downloadExport } from '../lib/exchange';
  import { Plus, Pin, Trash2, ExternalLink, Download, Square, CheckSquare } from '@lucide/svelte';

  // Live IndexedDB queries — re-run automatically when the data changes.
  const groupsQuery = liveQuery(() => db.groups.orderBy('displayName').toArray());
  const templatesQuery = liveQuery(() => db.templates.orderBy('name').toArray());
  const tagsQuery = knownTagsQuery();

  let groups = $state<TargetGroup[] | undefined>(undefined);
  let templates = $state<Array<{ id?: number; name: string }>>([]);
  let knownTags = $state<string[]>([]);

  $effect(() => {
    const sub = groupsQuery.subscribe({ next: (v) => (groups = v) });
    return () => sub.unsubscribe();
  });
  $effect(() => {
    const sub = templatesQuery.subscribe({ next: (v) => (templates = v) });
    return () => sub.unsubscribe();
  });
  $effect(() => {
    const sub = tagsQuery.subscribe({ next: (v) => (knownTags = v) });
    return () => sub.unsubscribe();
  });

  function addTag(tag: string) {
    const cur = tagsInput.trim();
    tagsInput = cur ? `${cur} ${tag}` : tag;
  }

  function addMarker(m: string) {
    const cur = markersInput.trim();
    markersInput = cur ? `${cur} ${m}` : m;
  }

  // All markers used across groups, for the suggestion chips.
  const knownMarkers = $derived.by(() => {
    const set = new Set<string>();
    for (const g of groups ?? []) for (const m of g.markers ?? []) set.add(m);
    return [...set].sort((a, b) => a.localeCompare(b));
  });

  // Honour an "open this group" request (e.g. a link from the Templates tab).
  $effect(() => {
    const id = nav.requestedGroupId;
    if (id == null || !groups) return;
    const g = groups.find((x) => x.id === id);
    if (g) {
      nav.requestedGroupId = null;
      void edit(g);
    }
  });

  let editing = $state<TargetGroup | null>(null);
  let tagsInput = $state('');
  let markersInput = $state('');
  let saveStatus = $state<AutosaveStatus>('idle');

  // ---- Search (by group name, alias, or assigned template name) ------------
  let search = $state('');
  const filteredGroups = $derived.by(() => {
    const q = search.trim().toLowerCase();
    const list = (groups ?? []).filter(
      (g) =>
        !q ||
        g.displayName.toLowerCase().includes(q) ||
        g.screenName.toLowerCase().includes(q) ||
        templateName(g.postTemplateId).toLowerCase().includes(q) ||
        (g.markers ?? []).some((m) => m.toLowerCase().includes(q)),
    );
    // Pinned groups float to the top; otherwise keep the query's name order.
    return [...list].sort(
      (a, b) => (b.pinned ? 1 : 0) - (a.pinned ? 1 : 0) || a.displayName.localeCompare(b.displayName),
    );
  });

  // Keep editing.mandatoryTags in sync with the input as the user types.
  $effect(() => {
    if (!editing) return;
    editing.mandatoryTags = tagsInput
      .split(/\s+/)
      .map((t) => t.replace(/^#+/, '').trim())
      .filter(Boolean);
  });

  // Keep editing.markers in sync with its input.
  $effect(() => {
    if (!editing) return;
    editing.markers = markersInput
      .split(/[\s,]+/)
      .map((t) => t.replace(/^#+/, '').trim())
      .filter(Boolean);
  });

  const autosave = createAutosave<TargetGroup>({
    get: () => editing,
    save: async (snap) => {
      await saveGroup(snap);
    },
    snapshot: (v) => $state.snapshot(v) as TargetGroup,
    delayMs: 500,
    onStatus: (s) => (saveStatus = s),
  });
  $effect(autosave.watch);

  async function edit(g: TargetGroup) {
    await autosave.flush();
    editing = { ...g };
    tagsInput = (g.mandatoryTags ?? []).join(' ');
    markersInput = (g.markers ?? []).join(' ');
    autosave.reset();
  }

  async function close() {
    await autosave.flush();
    editing = null;
  }

  async function addNew() {
    await autosave.flush();
    const id = await createGroup('');
    const g = await db.groups.get(id);
    if (g) await edit(g);
  }

  // ---- Bulk select ---------------------------------------------------------
  let bulkMode = $state(false);
  let bulkSel = $state<Set<number>>(new Set());

  function toggleBulkMode() {
    bulkMode = !bulkMode;
    bulkSel = new Set();
  }
  function toggleBulk(id: number) {
    const next = new Set(bulkSel);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    bulkSel = next;
  }
  async function bulkDelete() {
    const ids = [...bulkSel];
    if (ids.length === 0) return;
    const snaps = ((await Promise.all(ids.map((id) => db.groups.get(id)))).filter(Boolean)) as TargetGroup[];
    await Promise.all(ids.map((id) => deleteGroup(id)));
    if (editing && editing.id != null && bulkSel.has(editing.id)) editing = null;
    bulkSel = new Set();
    bulkMode = false;
    undo.offer(`Deleted ${snaps.length} group${snaps.length === 1 ? '' : 's'}`, async () => {
      await Promise.all(snaps.map((s) => db.groups.put(s)));
    });
  }
  async function bulkExport() {
    const ids = [...bulkSel];
    if (ids.length === 0) return;
    const data = await exportSubset({ templateIds: [], groupIds: ids, draftIds: [] });
    const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    downloadExport(data, `vk-postman-groups-${stamp}.json`);
  }

  async function removeGroup(g: TargetGroup) {
    if (!g.id) return;
    const snap = $state.snapshot(g) as TargetGroup;
    await deleteGroup(g.id);
    if (editing?.id === g.id) editing = null;
    undo.offer(`Deleted group “${snap.displayName}”`, async () => { await db.groups.put(snap); });
  }

  function templateName(id?: number): string {
    if (id == null) return '(none)';
    return templates.find((t) => t.id === id)?.name ?? '(deleted)';
  }

  const statusLabel = $derived.by(() => {
    switch (saveStatus) {
      case 'dirty':  return '…';
      case 'saving': return 'Saving…';
      case 'saved':  return '✓ Saved';
      case 'error':  return '⚠ Save failed';
      default:       return '';
    }
  });
</script>

<div class="editor-layout">
  <aside class="card">
    <div class="card-header">
      <h3 style="margin: 0;">Groups</h3>
      <div class="row">
        {#if groups && groups.length > 0}
          <button class="btn btn-ghost btn-sm" onclick={toggleBulkMode}>
            {bulkMode ? 'Done' : 'Select'}
          </button>
        {/if}
        <button class="btn btn-primary btn-sm" onclick={addNew}><Plus size={15} /> Add</button>
      </div>
    </div>

    {#if bulkMode && bulkSel.size > 0}
      <div class="bulk-bar">
        <span>{bulkSel.size} selected</span>
        <button class="btn btn-outline btn-sm" onclick={bulkExport}><Download size={14} /> Export</button>
        <button class="btn btn-danger btn-sm" onclick={bulkDelete}><Trash2 size={14} /> Delete</button>
      </div>
    {/if}

    {#if !groups}
      <p class="muted">Loading…</p>
    {:else if groups.length === 0}
      <p class="muted">No groups yet. Click <em>+ Add</em> to create one.</p>
    {:else}
      <input
        type="text"
        class="search-input"
        placeholder="Search name, alias, template…"
        bind:value={search}
        aria-label="Search groups"
      />
      {#if filteredGroups.length === 0}
        <p class="muted">No groups match “{search}”.</p>
      {:else}
        <div class="list">
          {#each filteredGroups as g (g.id)}
            <button
              class="list-item"
              class:active={editing?.id === g.id || (bulkMode && bulkSel.has(g.id!))}
              onclick={() => { if (g.id == null) return; bulkMode ? toggleBulk(g.id) : edit(g); }}
            >
              <strong>{#if bulkMode}{#if bulkSel.has(g.id!)}<CheckSquare size={14} class="inline-ico" />{:else}<Square size={14} class="inline-ico" />{/if}{/if}{#if g.pinned}<Pin size={13} class="inline-ico" />{/if}{g.displayName}</strong>
              <span class="meta">
                @{g.screenName} · template: <em>{templateName(g.postTemplateId)}</em>
              </span>
              {#if (g.markers ?? []).length > 0}
                <span class="marker-row">
                  {#each g.markers ?? [] as m (m)}<span class="marker-chip">{m}</span>{/each}
                </span>
              {/if}
            </button>
          {/each}
        </div>
      {/if}
    {/if}
  </aside>

  <section class="card" style="grid-column: span 2;">
    {#if !editing}
      <p class="muted" style="text-align: center; padding: 2rem 0;">
        Pick a group on the left, or click <em>+ Add</em>.
      </p>
    {:else}
      <div class="card-header">
        <h3 style="margin: 0;">Edit group</h3>
        <div class="row">
          <span class="muted" style="min-width: 5rem; text-align: right;">{statusLabel}</span>
          <button class="btn btn-ghost btn-sm" onclick={close}>Close</button>
        </div>
      </div>

      <div class="stack-lg">
        <div class="stack">
          <label for="g-display">Display name</label>
          <input id="g-display" type="text" bind:value={editing.displayName} />
        </div>
        <div class="stack">
          <label for="g-screen">Screen name</label>
          <input
            id="g-screen"
            type="text"
            placeholder="nelfias (no @, no vk.com/)"
            bind:value={editing.screenName}
          />
        </div>
        <div class="stack">
          <label for="g-template">Template</label>
          <div class="row" style="gap: 0.4rem; align-items: stretch;">
            <div class="grow">
              <SearchSelect
                id="g-template"
                value={editing.postTemplateId}
                items={templates.map((t) => ({ id: t.id, label: t.name }))}
                searchPlaceholder="Search templates…"
                onchange={(tid) => (editing!.postTemplateId = tid)}
              />
            </div>
            {#if editing.postTemplateId != null}
              <button
                type="button"
                class="btn btn-outline btn-sm"
                title="Open this template"
                onclick={() => nav.openTemplate(editing!.postTemplateId!)}
              ><ExternalLink size={15} /> Open</button>
            {/if}
          </div>
          <span class="muted">A group without a template can't be a draft target.</span>
        </div>
        <div class="stack">
          <label for="g-tags">Mandatory tags (space-separated)</label>
          <input id="g-tags" type="text" bind:value={tagsInput} />
          <TagSuggestions tags={knownTags} current={tagsInput} onpick={addTag} />
          <span class="muted">Appended to posts via <code>{'{{ group_tags }}'}</code>.</span>
        </div>
        <div class="stack">
          <label for="g-markers">Markers (labels for organising/filtering)</label>
          <input id="g-markers" type="text" placeholder="cosplay street portrait…" bind:value={markersInput} />
          <TagSuggestions tags={knownMarkers} current={markersInput} onpick={addMarker} />
          <span class="muted">Not posted — used to filter groups (e.g. on the Drafts page).</span>
        </div>
        <label class="row" style="gap: 0.5rem; font-weight: 500;">
          <input type="checkbox" bind:checked={editing.isActive} />
          <span>Active</span>
        </label>
        <label class="row" style="gap: 0.5rem; font-weight: 500;">
          <input
            type="checkbox"
            checked={editing.pinned ?? false}
            onchange={(e) => (editing!.pinned = (e.currentTarget as HTMLInputElement).checked)}
          />
          <span style="display: inline-flex; align-items: center; gap: 0.3rem;"><Pin size={15} /> Pin to top</span>
        </label>
        <div class="stack">
          <label for="g-notes">Notes</label>
          <textarea id="g-notes" bind:value={editing.notes}></textarea>
        </div>

        {#if editing.id != null}
          <div>
            <button class="btn btn-danger btn-sm" onclick={() => removeGroup(editing!)}>
              <Trash2 size={15} /> Delete group
            </button>
          </div>
        {/if}
      </div>
    {/if}
  </section>
</div>
