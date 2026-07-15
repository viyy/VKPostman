<script lang="ts">
  import { liveQuery } from 'dexie';
  import { db, createDraft, duplicateDraft, saveDraft, deleteDraft } from '../lib/db';
  import { exportSubset, downloadExport } from '../lib/exchange';
  import {
    packWikiLink,
    renderDraftForGroup,
    renderVkHtml,
    splitWikiLink,
    unionedPlaceholders,
    VK_POST_CHAR_LIMIT,
  } from '../lib/render';
  import {
    PlaceholderType,
    type PlaceholderDefinition,
    type PostDraft,
    type PostTemplate,
    type TargetGroup,
  } from '../lib/types';
  import { nav } from '../lib/nav.svelte';
  import { createAutosave, type AutosaveStatus } from '../lib/autosave';
  import { knownTagsQuery } from '../lib/tags';
  import { undo } from '../lib/undo.svelte';
  import { t } from '../lib/i18n.svelte';
  import TagSuggestions from './TagSuggestions.svelte';
  import {
    Plus, Copy, Files, Pin, Trash2, Check, Undo2, ExternalLink,
    GripVertical, Image, X, ChevronDown, TriangleAlert, Eye, EyeOff, Download, Square, CheckSquare,
  } from '@lucide/svelte';

  /** Collapsed state for the collapsible blocks (persists across reloads). */
  let groupsCollapsed  = $state(localStorage.getItem('vkp.draftGroupsCollapsed') === '1');
  let detailsCollapsed = $state(localStorage.getItem('vkp.draftDetailsCollapsed') === '1');
  let toPostCollapsed  = $state(localStorage.getItem('vkp.draftToPostCollapsed') === '1');
  let postedCollapsed  = $state(localStorage.getItem('vkp.draftPostedCollapsed') === '1');
  // "used by:" lines under placeholders — default collapsed (visual noise).
  let usedByCollapsed  = $state(localStorage.getItem('vkp.draftUsedByCollapsed') !== '0');
  $effect(() => {
    localStorage.setItem('vkp.draftGroupsCollapsed', groupsCollapsed ? '1' : '0');
  });
  $effect(() => {
    localStorage.setItem('vkp.draftDetailsCollapsed', detailsCollapsed ? '1' : '0');
  });
  $effect(() => {
    localStorage.setItem('vkp.draftToPostCollapsed', toPostCollapsed ? '1' : '0');
  });
  $effect(() => {
    localStorage.setItem('vkp.draftPostedCollapsed', postedCollapsed ? '1' : '0');
  });
  $effect(() => {
    localStorage.setItem('vkp.draftUsedByCollapsed', usedByCollapsed ? '1' : '0');
  });

  let saveStatus = $state<AutosaveStatus>('idle');

  // Show rendered posts as VK-style preview (links) vs raw copyable text.
  let vkPreview = $state(localStorage.getItem('vkp.vkPreview') === '1');
  $effect(() => {
    localStorage.setItem('vkp.vkPreview', vkPreview ? '1' : '0');
  });

  // ---- Draft list search + status filter -----------------------------------
  type StatusFilter = 'all' | 'active' | 'posted';
  let search = $state('');
  let statusFilter = $state<StatusFilter>(
    (localStorage.getItem('vkp.draftStatusFilter') as StatusFilter | null) ?? 'all',
  );
  $effect(() => {
    localStorage.setItem('vkp.draftStatusFilter', statusFilter);
  });

  // ---- Live data -----------------------------------------------------------
  const draftsQuery    = liveQuery(() => db.drafts.orderBy('updatedAt').reverse().toArray());
  const groupsQuery    = liveQuery(() => db.groups.orderBy('displayName').toArray());
  const templatesQuery = liveQuery(() => db.templates.toArray());
  const libraryQuery   = liveQuery(() => db.placeholders.toArray());
  const tagsQuery      = knownTagsQuery();

  let drafts    = $state<PostDraft[] | undefined>(undefined);
  let groups    = $state<TargetGroup[]>([]);
  let templates = $state<PostTemplate[]>([]);
  let library   = $state<PlaceholderDefinition[]>([]);
  let knownTags = $state<string[]>([]);

  $effect(() => {
    const s = draftsQuery.subscribe({ next: (v) => (drafts = v) });
    return () => s.unsubscribe();
  });
  $effect(() => {
    const s = groupsQuery.subscribe({ next: (v) => (groups = v) });
    return () => s.unsubscribe();
  });
  $effect(() => {
    const s = templatesQuery.subscribe({ next: (v) => (templates = v) });
    return () => s.unsubscribe();
  });
  $effect(() => {
    const s = libraryQuery.subscribe({ next: (v) => (library = v) });
    return () => s.unsubscribe();
  });
  $effect(() => {
    const s = tagsQuery.subscribe({ next: (v) => (knownTags = v) });
    return () => s.unsubscribe();
  });

  function addTag(tag: string) {
    const cur = themeTagsInput.trim();
    themeTagsInput = cur ? `${cur} ${tag}` : tag;
  }

  // ---- Target-group marker filter ------------------------------------------
  let groupMarkerFilter = $state<Set<string>>(new Set());
  const availableMarkers = $derived.by(() => {
    const set = new Set<string>();
    for (const g of groups) for (const m of g.markers ?? []) set.add(m);
    return [...set].sort((a, b) => a.localeCompare(b));
  });
  const pickGroups = $derived.by(() => {
    if (groupMarkerFilter.size === 0) return groups;
    return groups.filter((g) => (g.markers ?? []).some((m) => groupMarkerFilter.has(m)));
  });
  function toggleMarkerFilter(m: string) {
    const next = new Set(groupMarkerFilter);
    if (next.has(m)) next.delete(m);
    else next.add(m);
    groupMarkerFilter = next;
  }

  // ---- Image attachment notes (filenames/paths only) -----------------------
  let imageNoteInput = $state('');
  let dragOver = $state(false);

  function addImageNotes(names: string[]) {
    if (!draft) return;
    const cleaned = names.map((n) => n.trim()).filter(Boolean);
    if (cleaned.length === 0) return;
    const existing = new Set(draft.imageNotes ?? []);
    const merged = [...(draft.imageNotes ?? [])];
    for (const n of cleaned) if (!existing.has(n)) merged.push(n);
    draft.imageNotes = merged;
  }

  function addImageNoteFromInput() {
    if (!imageNoteInput.trim()) return;
    addImageNotes([imageNoteInput]);
    imageNoteInput = '';
  }

  function removeImageNote(name: string) {
    if (!draft) return;
    draft.imageNotes = (draft.imageNotes ?? []).filter((n) => n !== name);
  }

  /** For each image filename in the current draft, list other drafts that also
   * reference it — feeds an inline "duplicate photo" warning next to the row. */
  const duplicatedImages = $derived.by(() => {
    const map = new Map<string, Array<{ id: number; title: string; fullyPosted: boolean }>>();
    if (!draft || !drafts) return map;
    const currentImages = new Set(draft.imageNotes ?? []);
    if (currentImages.size === 0) return map;
    for (const d of drafts) {
      if (d.id === draft.id) continue;
      for (const n of d.imageNotes ?? []) {
        if (!currentImages.has(n)) continue;
        const list = map.get(n) ?? [];
        list.push({ id: d.id!, title: d.title, fullyPosted: isFullyPosted(d) });
        map.set(n, list);
      }
    }
    return map;
  });

  function onImageDrop(e: DragEvent) {
    e.preventDefault();
    dragOver = false;
    const files = e.dataTransfer?.files;
    if (files && files.length > 0) {
      addImageNotes([...files].map((f) => f.name));
    } else {
      // Plain-text drops (e.g. a path dragged from another field).
      const text = e.dataTransfer?.getData('text');
      if (text) addImageNotes(text.split(/[\r\n]+/));
    }
  }

  // ---- Selection -----------------------------------------------------------
  let currentId = $state<number | null>(null);
  let draft = $state<PostDraft | null>(null);
  let themeTagsInput = $state('');

  // Load the chosen draft into the local editable copy.
  $effect(() => {
    if (currentId == null) {
      draft = null;
      return;
    }
    void (async () => {
      const d = await db.drafts.get(currentId);
      if (!d) {
        draft = null;
        return;
      }
      draft = {
        ...d,
        placeholderValues: { ...d.placeholderValues },
        themeTags: [...d.themeTags],
        targetGroupIds: [...d.targetGroupIds],
        // Older drafts predate posted tracking — default to empty.
        postedGroupIds: [...(d.postedGroupIds ?? [])],
        postedAt: { ...(d.postedAt ?? {}) },
        imageNotes: [...(d.imageNotes ?? [])],
        notes: d.notes ?? '',
        plannedFor: d.plannedFor ?? '',
      };
      themeTagsInput = draft.themeTags.join(' ');
      // Adopt the freshly-loaded draft as the autosave baseline so merely
      // opening it doesn't count as a change.
      autosave.reset();
    })();
  });

  // Auto-pick the first draft when the list arrives.
  $effect(() => {
    if (currentId == null && drafts && drafts.length > 0) {
      currentId = drafts[0].id!;
    }
  });

  // Honour an "open this draft" request (cross-tab links / global search).
  $effect(() => {
    const id = nav.requestedDraftId;
    if (id == null) return;
    nav.requestedDraftId = null;
    void selectDraft(id);
  });

  // ---- Autosave ------------------------------------------------------------
  const autosave = createAutosave<PostDraft>({
    get: () => draft,
    save: async (snap) => { await saveDraft(snap); },
    snapshot: (v) => $state.snapshot(v) as PostDraft,
    delayMs: 500,
    onStatus: (s) => (saveStatus = s),
  });
  $effect(autosave.watch);

  const statusLabel = $derived.by(() => {
    switch (saveStatus) {
      case 'dirty':  return '…';
      case 'saving': return t('Saving…');
      case 'saved':  return t('✓ Saved');
      case 'error':  return t('⚠ Save failed');
      default:       return '';
    }
  });

  // Keep draft.themeTags in sync with the input as the user types, so the
  // per-group preview re-renders live. Previously this parse only ran in
  // save(), so previews only updated after a round-trip to IndexedDB.
  $effect(() => {
    if (!draft) return;
    draft.themeTags = themeTagsInput
      .split(/\s+/)
      .map((t) => t.replace(/^#+/, '').trim())
      .filter(Boolean);
  });

  // ---- Derived data --------------------------------------------------------
  const templatesById = $derived(
    new Map<number, PostTemplate>(templates.filter((t) => t.id != null).map((t) => [t.id!, t]))
  );

  const libraryByKey = $derived(
    new Map<string, PlaceholderDefinition>(library.map((d) => [d.key, d]))
  );

  // Ordered by targetGroupIds so the render/queue order is the user's chosen
  // order (drag-reorderable in the "To post" list), not DB/display order.
  const selectedGroups = $derived.by(() => {
    if (!draft) return [] as TargetGroup[];
    const byId = new Map(groups.map((g) => [g.id!, g]));
    return draft.targetGroupIds
      .map((id) => byId.get(id))
      .filter((g): g is TargetGroup => g != null);
  });

  const placeholders = $derived(
    unionedPlaceholders(selectedGroups, templatesById, libraryByKey)
  );

  // Detailed readiness checks — what (if anything) is still missing.
  const validationIssues = $derived.by(() => {
    if (!draft) return [] as string[];
    const issues: string[] = [];
    if (selectedGroups.length === 0) issues.push(t('No target groups selected.'));
    for (const g of selectedGroups) {
      if (g.postTemplateId == null) issues.push(`${g.displayName}: ${t('no template assigned')}`);
    }
    for (const u of placeholders) {
      const def = u.definition;
      const raw = draft.placeholderValues[u.key] ?? '';
      const filled =
        def?.type === PlaceholderType.WikiLink
          ? splitWikiLink(raw).target.trim().length > 0
          : raw.trim().length > 0;
      const hasDefault = !!(def?.defaultValue && def.defaultValue.trim());
      if (!filled && !hasDefault) {
        issues.push(t('Missing value: {name} ({groups})', { name: def?.displayName ?? u.key, groups: u.usedByGroups.join(', ') }));
      }
    }
    return issues;
  });

  const ready = $derived(draft != null && validationIssues.length === 0);

  // Per-group rendered output, re-computed whenever relevant state changes.
  const renders = $derived.by(() => {
    if (!draft) return [];
    return selectedGroups.map((g) => {
      const tpl = g.postTemplateId != null ? templatesById.get(g.postTemplateId) : undefined;
      if (!tpl) return { group: g, text: '[This group has no template assigned.]' };
      try {
        return { group: g, text: renderDraftForGroup(draft!, g, tpl, libraryByKey) };
      } catch (err) {
        return { group: g, text: `[Render error: ${(err as Error).message}]` };
      }
    });
  });

  /** Split rendered cards into not-yet-posted and posted, driven by draft.postedGroupIds. */
  const activeRenders = $derived(
    renders.filter((r) => !(draft?.postedGroupIds ?? []).includes(r.group.id!))
  );
  const postedRenders = $derived(
    renders.filter((r) => (draft?.postedGroupIds ?? []).includes(r.group.id!))
  );

  // Posted summary for the open draft: "posted to N/M groups, last on <date>".
  const postedSummary = $derived.by(() => {
    if (!draft) return null;
    const targets = draft.targetGroupIds;
    const postedIds = targets.filter((id) => draft!.postedGroupIds.includes(id));
    let last: Date | null = null;
    for (const id of postedIds) {
      const iso = draft.postedAt?.[id];
      if (!iso) continue;
      const dt = new Date(iso);
      if (!last || dt > last) last = dt;
    }
    return { total: targets.length, posted: postedIds.length, last };
  });

  // ---- Draft list filtering (search + status) ------------------------------
  const groupsById = $derived(new Map(groups.map((g) => [g.id!, g])));

  /** A draft counts as "posted" once it has targets and all are marked posted. */
  function isFullyPosted(d: PostDraft): boolean {
    const targets = d.targetGroupIds ?? [];
    if (targets.length === 0) return false;
    const posted = d.postedGroupIds ?? [];
    return targets.every((id) => posted.includes(id));
  }

  /** Posted/total target groups for a draft (drives the list progress bar). */
  function progressOf(d: PostDraft): { posted: number; total: number } {
    const targets = d.targetGroupIds ?? [];
    const posted = d.postedGroupIds ?? [];
    return { posted: targets.filter((id) => posted.includes(id)).length, total: targets.length };
  }

  const filteredDrafts = $derived.by(() => {
    const list = drafts ?? [];
    const q = search.trim().toLowerCase();
    const matched = list.filter((d) => {
      // Status filter.
      if (statusFilter === 'posted' && !isFullyPosted(d)) return false;
      if (statusFilter === 'active' && isFullyPosted(d)) return false;
      // Text search: title, common text, theme tags, target group name/alias.
      if (!q) return true;
      if (d.title.toLowerCase().includes(q)) return true;
      if (d.commonText.toLowerCase().includes(q)) return true;
      if (d.themeTags.some((t) => t.toLowerCase().includes(q))) return true;
      return (d.targetGroupIds ?? []).some((id) => {
        const g = groupsById.get(id);
        return (
          !!g &&
          (g.displayName.toLowerCase().includes(q) ||
            g.screenName.toLowerCase().includes(q))
        );
      });
    });
    // Pinned drafts float to the top; the query already orders by recency.
    return [...matched].sort((a, b) => (b.pinned ? 1 : 0) - (a.pinned ? 1 : 0));
  });

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
    const snaps = ((await Promise.all(ids.map((id) => db.drafts.get(id)))).filter(Boolean)) as PostDraft[];
    await Promise.all(ids.map((id) => deleteDraft(id)));
    if (currentId != null && bulkSel.has(currentId)) currentId = null;
    bulkSel = new Set();
    bulkMode = false;
    undo.offer(`Deleted ${snaps.length} draft${snaps.length === 1 ? '' : 's'}`, async () => {
      await Promise.all(snaps.map((s) => db.drafts.put(s)));
    });
  }
  async function bulkExport() {
    const ids = [...bulkSel];
    if (ids.length === 0) return;
    const data = await exportSubset({ templateIds: [], groupIds: [], draftIds: ids });
    const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    downloadExport(data, `vk-postman-drafts-${stamp}.json`);
  }

  // ---- Commands ------------------------------------------------------------
  async function newDraft() {
    await autosave.flush();
    const id = await createDraft();
    currentId = id;
  }

  /** Switch drafts — flush the current one first so its pending save lands. */
  async function selectDraft(id: number) {
    await autosave.flush();
    currentId = id;
  }

  async function remove() {
    if (!draft?.id) return;
    const snap = $state.snapshot(draft) as PostDraft;
    await deleteDraft(draft.id);
    currentId = null;
    undo.offer(`Deleted draft “${snap.title}”`, async () => {
      await db.drafts.put(snap);
      currentId = snap.id ?? null;
    });
  }

  function togglePin() {
    if (!draft) return;
    draft.pinned = !draft.pinned;
  }

  /** Clone the current draft (keeps content, resets posted progress). */
  async function duplicate() {
    if (!draft?.id) return;
    await autosave.flush();
    currentId = await duplicateDraft(draft.id);
  }

  function markPosted(groupId: number) {
    if (!draft) return;
    if (!draft.postedGroupIds.includes(groupId)) {
      draft.postedGroupIds = [...draft.postedGroupIds, groupId];
    }
    // Record/refresh the timestamp for this group.
    draft.postedAt = { ...(draft.postedAt ?? {}), [groupId]: new Date().toISOString() };
  }

  function unmarkPosted(groupId: number) {
    if (!draft) return;
    draft.postedGroupIds = draft.postedGroupIds.filter((id) => id !== groupId);
    if (draft.postedAt) {
      const { [groupId]: _drop, ...rest } = draft.postedAt;
      draft.postedAt = rest;
    }
  }

  /** Human label for when a group was marked posted (or '' if unknown). */
  function postedDateLabel(groupId: number): string {
    const iso = draft?.postedAt?.[groupId];
    if (!iso) return '';
    return new Date(iso).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  }

  // ---- Drag-to-reorder the target groups (affects "Copy next & open") ------
  let dragGroupId = $state<number | null>(null);

  function reorderTo(targetId: number) {
    if (!draft || dragGroupId == null || dragGroupId === targetId) return;
    const ids = [...draft.targetGroupIds];
    const from = ids.indexOf(dragGroupId);
    const to = ids.indexOf(targetId);
    if (from < 0 || to < 0) return;
    ids.splice(from, 1);
    ids.splice(to, 0, dragGroupId);
    draft.targetGroupIds = ids;
    dragGroupId = null;
  }

  function toggleGroup(g: TargetGroup) {
    if (!draft || g.id == null) return;
    const i = draft.targetGroupIds.indexOf(g.id);
    if (i >= 0) draft.targetGroupIds.splice(i, 1);
    else draft.targetGroupIds.push(g.id);
    // Force reactivity update:
    draft.targetGroupIds = [...draft.targetGroupIds];
  }

  async function copyText(text: string) {
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      // Fallback for older browsers / insecure contexts.
      const ta = document.createElement('textarea');
      ta.value = text;
      document.body.appendChild(ta);
      ta.select();
      document.execCommand('copy');
      document.body.removeChild(ta);
    }
  }

  function openGroup(g: TargetGroup) {
    window.open(`https://vk.com/${g.screenName}`, '_blank', 'noopener');
  }

  /**
   * Copy the next not-yet-posted group's rendered text and open its vk.com
   * page — the core "work through the queue" action. Copy first (while the
   * document still has focus, as the Clipboard API requires) then open.
   */
  async function copyNextAndOpen() {
    const next = activeRenders[0];
    if (!next) return;
    await copyText(next.text);
    openGroup(next.group);
  }

  /** Open every not-yet-posted group's vk.com page (popup blocker may limit this). */
  function openAllUnposted() {
    for (const r of activeRenders) {
      window.open(`https://vk.com/${r.group.screenName}`, '_blank', 'noopener');
    }
  }

  // WikiLink value split helpers — the packed value lives in placeholderValues.
  function wikiTarget(key: string): string {
    return splitWikiLink(draft?.placeholderValues[key]).target;
  }
  function wikiDisplay(key: string): string {
    return splitWikiLink(draft?.placeholderValues[key]).display;
  }
  function setWiki(key: string, which: 'target' | 'display', value: string) {
    if (!draft) return;
    const parts = splitWikiLink(draft.placeholderValues[key]);
    parts[which] = value;
    draft.placeholderValues[key] = packWikiLink(parts.target, parts.display);
    draft.placeholderValues = { ...draft.placeholderValues };
  }
  function setPlain(key: string, value: string) {
    if (!draft) return;
    draft.placeholderValues[key] = value;
    draft.placeholderValues = { ...draft.placeholderValues };
  }

  function typeLabel(t: PlaceholderType): string {
    switch (t) {
      case PlaceholderType.VkLink: return 'VK link';
      case PlaceholderType.WikiLink: return 'wiki link';
      case PlaceholderType.Url: return 'URL';
      case PlaceholderType.TagList: return 'tags';
      default: return 'text';
    }
  }
</script>

<div class="editor-layout">
  <!-- ==== Draft list ==== -->
  <aside class="card">
    <div class="card-header">
      <h3 style="margin: 0;">{t('Drafts')}</h3>
      <div class="row">
        {#if drafts && drafts.length > 0}
          <button class="btn btn-ghost btn-sm" onclick={toggleBulkMode}>
            {bulkMode ? t('Done') : t('Select')}
          </button>
        {/if}
        <button class="btn btn-primary btn-sm" onclick={newDraft}><Plus size={15} /> {t('New')}</button>
      </div>
    </div>

    {#if bulkMode && bulkSel.size > 0}
      <div class="bulk-bar">
        <span>{t('{n} selected', { n: bulkSel.size })}</span>
        <button class="btn btn-outline btn-sm" onclick={bulkExport}><Download size={14} /> {t('Export')}</button>
        <button class="btn btn-danger btn-sm" onclick={bulkDelete}><Trash2 size={14} /> {t('Delete')}</button>
      </div>
    {/if}

    {#if !drafts}
      <p class="muted">{t('Loading…')}</p>
    {:else if drafts.length === 0}
      <p class="muted">{t('No drafts yet.')}</p>
    {:else}
      <input
        type="text"
        class="search-input"
        placeholder={t('Search title, text, group…')}
        bind:value={search}
        aria-label="Search drafts"
      />
      <div class="seg" role="tablist" aria-label="Filter by status">
        <button
          class="seg-btn" class:active={statusFilter === 'all'}
          onclick={() => (statusFilter = 'all')}
        >{t('All')}</button>
        <button
          class="seg-btn" class:active={statusFilter === 'active'}
          onclick={() => (statusFilter = 'active')}
        >{t('Pending')}</button>
        <button
          class="seg-btn" class:active={statusFilter === 'posted'}
          onclick={() => (statusFilter = 'posted')}
        >{t('Posted')}</button>
      </div>

      {#if filteredDrafts.length === 0}
        <p class="muted">{t('No drafts match the current filter.')}</p>
      {:else}
        <div class="list">
          {#each filteredDrafts as d (d.id)}
            <button
              class="list-item"
              class:active={currentId === d.id || (bulkMode && bulkSel.has(d.id!))}
              onclick={() => { if (d.id == null) return; bulkMode ? toggleBulk(d.id) : selectDraft(d.id); }}
            >
              <strong>
                {#if bulkMode}{#if bulkSel.has(d.id!)}<CheckSquare size={14} class="inline-ico" />{:else}<Square size={14} class="inline-ico" />{/if}{/if}{#if d.pinned}<Pin size={13} class="inline-ico" />{/if}{#if isFullyPosted(d)}<Check size={14} class="inline-ico done-ico" />{/if}{d.title}
              </strong>
              <span class="meta">{new Date(d.updatedAt).toLocaleString()}</span>
              {#if progressOf(d).total > 0}
                {@const p = progressOf(d)}
                <span class="draft-progress" title={`Posted to ${p.posted} of ${p.total} groups`}>
                  <span
                    class="draft-progress-fill"
                    class:done={p.posted === p.total}
                    style="width: {(p.posted / p.total) * 100}%;"
                  ></span>
                </span>
              {/if}
            </button>
          {/each}
        </div>
      {/if}
    {/if}
  </aside>

  <!-- ==== Editor ==== -->
  <section>
    {#if !draft}
      <div class="card">
        <p class="muted" style="text-align: center; padding: 2rem 0;">
          {t('Pick a draft on the left, or click')} <em>+ {t('New')}</em>.
        </p>
      </div>
    {:else}
      <div class="card">
        <div class="card-header">
          <button
            type="button"
            class="collapse-header"
            style="width: auto; margin-bottom: 0;"
            aria-expanded={!detailsCollapsed}
            onclick={() => (detailsCollapsed = !detailsCollapsed)}
          >
            <span class="collapse-chevron" class:collapsed={detailsCollapsed}><ChevronDown size={16} /></span>
            <h3 style="margin: 0; white-space: nowrap;">{t('Draft details')}</h3>
          </button>
          <div class="row" style="flex-wrap: nowrap;">
            <span class="pill" style={ready ? '' : 'background:var(--vk-hover); color:var(--vk-text-secondary);'}>
              {ready ? t('ready') : t('incomplete')}
            </span>
            <span class="muted" style="text-align: right;">{statusLabel}</span>
            <button
              class="btn btn-ghost btn-sm icon-only"
              class:pinned={draft.pinned}
              title={draft.pinned ? t('Unpin') : t('Pin to top')}
              aria-label={draft.pinned ? t('Unpin') : t('Pin to top')}
              onclick={togglePin}
            ><Pin size={15} /></button>
            <button
              class="btn btn-ghost btn-sm icon-only"
              title={t('Duplicate')}
              aria-label={t('Duplicate')}
              onclick={duplicate}
            ><Files size={15} /></button>
            <button
              class="btn btn-danger btn-sm icon-only"
              title={t('Delete')}
              aria-label={t('Delete')}
              onclick={remove}
            ><Trash2 size={15} /></button>
          </div>
        </div>

        {#if !ready && validationIssues.length > 0}
          <ul class="issues">
            {#each validationIssues as issue (issue)}
              <li><TriangleAlert size={13} class="inline-ico" /> {issue}</li>
            {/each}
          </ul>
        {/if}

        {#if !detailsCollapsed}
        <div class="stack-lg">
          <div class="stack">
            <label for="d-title">{t('Title')}</label>
            <input id="d-title" type="text" bind:value={draft.title} />
          </div>
          <div class="stack">
            <label for="d-common">{t('Common text')}</label>
            <textarea
              id="d-common"
              bind:value={draft.commonText}
              placeholder={'Rendered via {{ common_text }} in every template'}
            ></textarea>
          </div>
          <div class="stack">
            <label for="d-tags">{t('Theme tags (common to all groups)')}</label>
            <input id="d-tags" type="text" bind:value={themeTagsInput} />
            <TagSuggestions tags={knownTags} current={themeTagsInput} onpick={addTag} />
          </div>

          <div class="stack">
            <div class="field-label">
              {t('Images to attach')}
              <span class="muted">&nbsp;{t('(filenames/paths — a manual checklist, files aren’t stored)')}</span>
            </div>
            <div
              class="dropzone"
              class:drag-over={dragOver}
              role="button"
              tabindex="0"
              ondragover={(e) => { e.preventDefault(); dragOver = true; }}
              ondragleave={() => (dragOver = false)}
              ondrop={onImageDrop}
            >
              {t('Drop image files here to add their names')}
            </div>
            <div class="row" style="gap: 0.4rem;">
              <input
                type="text"
                class="grow"
                placeholder={t('…or type a filename/path and press Enter')}
                bind:value={imageNoteInput}
                onkeydown={(e) => { if (e.key === 'Enter') { e.preventDefault(); addImageNoteFromInput(); } }}
              />
              <button class="btn btn-outline btn-sm" onclick={addImageNoteFromInput}>+ {t('Add')}</button>
            </div>
            {#if (draft.imageNotes ?? []).length > 0}
              <ul class="image-list">
                {#each draft.imageNotes ?? [] as name (name)}
                  {@const dups = duplicatedImages.get(name)}
                  <li class:has-dup={dups && dups.length > 0}>
                    <span class="img-name"><Image size={14} class="inline-ico" /> {name}</span>
                    {#if dups && dups.length > 0}
                      <button
                        class="img-dup"
                        title={t('Also in: {list}', { list: dups.map((d) => d.fullyPosted ? `${d.title} (${t('Posted')})` : d.title).join(', ') })}
                        onclick={() => nav.openDraft(dups[0].id)}
                      ><TriangleAlert size={13} /></button>
                    {/if}
                    <button
                      class="img-remove"
                      aria-label={`Remove ${name}`}
                      onclick={() => removeImageNote(name)}
                    ><X size={14} /></button>
                  </li>
                {/each}
              </ul>
            {/if}
          </div>

          <div class="stack">
            <label for="d-notes">{t('Scratch notes')} <span class="muted">{t('(private, not posted)')}</span></label>
            <textarea
              id="d-notes"
              bind:value={draft.notes}
              placeholder={t('Reminders, ideas, to-dos for this post…')}
            ></textarea>
          </div>

          <div class="stack">
            <label for="d-plan">{t('Plan to post on')} <span class="muted">{t('(optional)')}</span></label>
            <div class="row" style="gap: 0.4rem;">
              <input id="d-plan" type="date" style="width: auto;" bind:value={draft.plannedFor} />
              {#if draft.plannedFor}
                <button class="btn btn-ghost btn-sm" onclick={() => (draft!.plannedFor = '')}>{t('Clear')}</button>
              {/if}
            </div>
          </div>
        </div>
        {/if}
      </div>

      <!-- Target groups (collapsible) -->
      <div class="card">
        <button
          type="button"
          class="collapse-header"
          aria-expanded={!groupsCollapsed}
          onclick={() => (groupsCollapsed = !groupsCollapsed)}
        >
          <span class="collapse-chevron" class:collapsed={groupsCollapsed}><ChevronDown size={16} /></span>
          <h3 style="margin: 0;">{t('Target groups')}</h3>
          <span class="muted" style="margin-left: auto;">
            {draft.targetGroupIds.length} / {groups.length}
          </span>
        </button>
        {#if !groupsCollapsed}
          {#if groups.length === 0}
            <p class="muted">
              {t('No groups yet — add some on the')} <strong>{t('Groups')}</strong> {t('tab.')}
            </p>
          {:else}
            {#if availableMarkers.length > 0}
              <div class="marker-filter">
                <span class="muted" style="font-size: 0.75rem;">{t('Filter by marker:')}</span>
                {#each availableMarkers as m (m)}
                  <button
                    type="button"
                    class="marker-chip filter"
                    class:on={groupMarkerFilter.has(m)}
                    onclick={() => toggleMarkerFilter(m)}
                  >{m}</button>
                {/each}
                {#if groupMarkerFilter.size > 0}
                  <button type="button" class="link-btn" onclick={() => (groupMarkerFilter = new Set())}>{t('Clear')}</button>
                {/if}
              </div>
            {/if}
            <div>
              {#each pickGroups as g (g.id)}
                {@const hasTpl = g.postTemplateId != null}
                <label class="pick-row" class:disabled={!hasTpl}>
                  <input
                    type="checkbox"
                    disabled={!hasTpl}
                    checked={draft.targetGroupIds.includes(g.id!)}
                    onchange={() => toggleGroup(g)}
                  />
                  <div class="grow">
                    <strong>{g.displayName}</strong>
                    <div class="muted">
                      @{g.screenName} · {#if hasTpl}
                        {t('template:')}
                        <button
                          type="button"
                          class="link-btn"
                          onclick={(e) => { e.preventDefault(); nav.openTemplate(g.postTemplateId!); }}
                        >{templatesById.get(g.postTemplateId!)?.name ?? '(deleted)'}</button>
                      {:else}
                        <span class="warn"><TriangleAlert size={13} class="inline-ico" /> {t('no template assigned')}</span>
                      {/if}
                    </div>
                    {#if (g.markers ?? []).length > 0}
                      <span class="marker-row">
                        {#each g.markers ?? [] as m (m)}<span class="marker-chip">{m}</span>{/each}
                      </span>
                    {/if}
                  </div>
                </label>
              {/each}
              {#if pickGroups.length === 0}
                <p class="muted">{t('No groups match the selected markers.')}</p>
              {/if}
            </div>
          {/if}
        {/if}
      </div>

      <!-- Placeholders (union) -->
      {#if placeholders.length > 0}
        <div class="card">
          <div class="card-header">
            <h3 style="margin: 0;">{t('Placeholders')}</h3>
            <div class="row">
              <span class="muted">{t('union across selected groups\' templates')}</span>
              <button
                type="button"
                class="link-btn"
                onclick={() => (usedByCollapsed = !usedByCollapsed)}
              >{usedByCollapsed ? t('show "used by"') : t('hide "used by"')}</button>
            </div>
          </div>
          <div class="stack-lg">
            {#each placeholders as u (u.key)}
              {@const def = u.definition}
              <div class="stack">
                <!-- Not a <label>: a WikiLink field has two inputs, so this is a
                     group heading, not a single-control label. -->
                <div class="field-label">
                  {def?.displayName ?? u.key}
                  <span class="muted">&nbsp;({typeLabel(def?.type ?? PlaceholderType.Text)})</span>
                </div>

                {#if def?.type === PlaceholderType.WikiLink}
                  <div class="row" style="gap: 0.5rem;">
                    <div class="stack grow">
                      <span class="muted">target (e.g. theowlettcosplay, nelfias_cosph)</span>
                      <input
                        type="text"
                        value={wikiTarget(u.key)}
                        oninput={(e) => setWiki(u.key, 'target', (e.target as HTMLInputElement).value)}
                      />
                    </div>
                    <div class="stack grow">
                      <span class="muted">displayed text (e.g. The Owlett ✦ Cosplay, Nelfias - Cosplay Photo СПб)</span>
                      <input
                        type="text"
                        value={wikiDisplay(u.key)}
                        oninput={(e) => setWiki(u.key, 'display', (e.target as HTMLInputElement).value)}
                      />
                    </div>
                  </div>
                {:else}
                  <input
                    type="text"
                    value={draft.placeholderValues[u.key] ?? ''}
                    oninput={(e) => setPlain(u.key, (e.target as HTMLInputElement).value)}
                  />
                {/if}

                {#if !usedByCollapsed}
                  <span class="muted">{t('used by: {names}', { names: u.usedByGroups.join(', ') })}</span>
                {/if}
              </div>
            {/each}
          </div>
        </div>
      {/if}
    {/if}
  </section>

  <!-- ==== Per-group output ==== -->
  <section>
    <div class="card-header" style="padding-left: 4px; flex-wrap: wrap;">
      <button
        type="button"
        class="collapse-header"
        style="width: auto; margin-bottom: 0;"
        aria-expanded={!toPostCollapsed}
        onclick={() => (toPostCollapsed = !toPostCollapsed)}
      >
        <span class="collapse-chevron" class:collapsed={toPostCollapsed}><ChevronDown size={16} /></span>
        <h3 style="margin: 0; white-space: nowrap;">{t('To post')}</h3>
      </button>
      <div class="row">
        {#if postedRenders.length > 0}
          <span class="muted">{t('{n} posted', { n: postedRenders.length })}</span>
        {/if}
        <button
          class="btn btn-ghost btn-sm"
          title={vkPreview ? t('Show raw copyable text') : t('Show VK-style preview (links)')}
          aria-pressed={vkPreview}
          onclick={() => (vkPreview = !vkPreview)}
        >{#if vkPreview}<EyeOff size={15} /> {t('Raw')}{:else}<Eye size={15} /> {t('Preview')}{/if}</button>
        <button
          class="btn btn-outline btn-sm"
          disabled={activeRenders.length === 0}
          title={t('Open all')}
          onclick={openAllUnposted}
        ><ExternalLink size={15} /> {t('Open all')}</button>
        <button
          class="btn btn-primary btn-sm"
          disabled={activeRenders.length === 0}
          title={t('Copy next & open')}
          onclick={copyNextAndOpen}
        ><Copy size={15} /> {t('Copy next & open')}</button>
      </div>
    </div>

    {#if postedSummary && postedSummary.total > 0}
      <p class="muted" style="margin: -0.2rem 0 0.6rem; padding-left: 4px;">
        {t('Posted to {a}/{b} groups', { a: postedSummary.posted, b: postedSummary.total })}{#if postedSummary.last} · {t('last on {date}', { date: postedSummary.last.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' }) })}{/if}
      </p>
    {/if}

    {#if !toPostCollapsed}
      {#if renders.length === 0}
        <p class="muted">{t('Pick one or more target groups on the left to see rendered posts.')}</p>
      {:else if activeRenders.length === 0}
        <p class="muted">{t('All selected groups are marked posted. 🎉')}</p>
      {:else}
        {#each activeRenders as r (r.group.id)}
          <div
            class="card"
            class:drop-target={dragGroupId != null && dragGroupId !== r.group.id}
            ondragover={(e) => { if (dragGroupId != null) e.preventDefault(); }}
            ondrop={() => reorderTo(r.group.id!)}
            role="listitem"
          >
            <div class="post-hdr">
              <strong class="post-title">
                <span
                  class="drag-handle"
                  draggable="true"
                  role="button"
                  tabindex="-1"
                  ondragstart={() => (dragGroupId = r.group.id!)}
                  ondragend={() => (dragGroupId = null)}
                  title={t('Drag to reorder')}
                  aria-label={t('Drag to reorder')}
                ><GripVertical size={15} /></span>
                {r.group.displayName}
              </strong>
              <div class="row post-actions">
                <button class="btn btn-outline btn-sm" onclick={() => copyText(r.text)}><Copy size={15} /> {t('Copy')}</button>
                <button class="btn btn-outline btn-sm" onclick={() => openGroup(r.group)}><ExternalLink size={15} /> {t('Open vk.com')}</button>
                <button class="btn btn-primary btn-sm" onclick={() => markPosted(r.group.id!)}><Check size={15} /> {t('Posted')}</button>
              </div>
            </div>
            {#if vkPreview}
              <div class="rendered">{@html renderVkHtml(r.text)}</div>
            {:else}
              <div class="rendered">{r.text}</div>
            {/if}
            <div class="char-count" class:over={r.text.length > VK_POST_CHAR_LIMIT}>
              {t('{n} chars', { n: r.text.length.toLocaleString() })}{#if r.text.length > VK_POST_CHAR_LIMIT}
                · <TriangleAlert size={12} class="inline-ico" /> {t('exceeds ~{n}', { n: VK_POST_CHAR_LIMIT.toLocaleString() })}{/if}
            </div>
          </div>
        {/each}
      {/if}
    {/if}

    <!-- Posted block -->
    {#if postedRenders.length > 0}
      <button
        type="button"
        class="collapse-header"
        style="padding-left: 4px; margin-top: 1rem;"
        aria-expanded={!postedCollapsed}
        onclick={() => (postedCollapsed = !postedCollapsed)}
      >
        <span class="collapse-chevron" class:collapsed={postedCollapsed}><ChevronDown size={16} /></span>
        <h3 style="margin: 0; display: inline-flex; align-items: center; gap: 0.35rem;"><Check size={17} /> {t('Posted')}</h3>
        <span class="muted" style="margin-left: auto;">{postedRenders.length}</span>
      </button>
      {#if !postedCollapsed}
        {#each postedRenders as r (r.group.id)}
          <div class="card posted-card">
            <div class="post-hdr">
              <strong class="post-title">
                {r.group.displayName}
                {#if postedDateLabel(r.group.id!)}
                  <span class="muted" style="font-weight: 400;">· {t('posted {date}', { date: postedDateLabel(r.group.id!) })}</span>
                {/if}
              </strong>
              <div class="row post-actions">
                <button class="btn btn-outline btn-sm" onclick={() => copyText(r.text)}><Copy size={15} /> {t('Copy')}</button>
                <button class="btn btn-outline btn-sm" onclick={() => openGroup(r.group)}><ExternalLink size={15} /> {t('Open vk.com')}</button>
                <button class="btn btn-ghost btn-sm" onclick={() => unmarkPosted(r.group.id!)}><Undo2 size={15} /> {t('Unmark')}</button>
              </div>
            </div>
            {#if vkPreview}
              <div class="rendered">{@html renderVkHtml(r.text)}</div>
            {:else}
              <div class="rendered">{r.text}</div>
            {/if}
            <div class="char-count" class:over={r.text.length > VK_POST_CHAR_LIMIT}>
              {t('{n} chars', { n: r.text.length.toLocaleString() })}{#if r.text.length > VK_POST_CHAR_LIMIT}
                · <TriangleAlert size={12} class="inline-ico" /> {t('exceeds ~{n}', { n: VK_POST_CHAR_LIMIT.toLocaleString() })}{/if}
            </div>
          </div>
        {/each}
      {/if}
    {/if}
  </section>
</div>

<!-- Ctrl/Cmd+Enter anywhere on the Drafts tab → copy next unposted & open. -->
<svelte:window
  onkeydown={(e) => {
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter' && activeRenders.length > 0) {
      e.preventDefault();
      void copyNextAndOpen();
    }
  }}
/>

<style>
  /* Dim posted cards so the eye lands on what's still to do. */
  .posted-card {
    opacity: 0.7;
  }
  .posted-card .rendered {
    max-height: 120px;
  }
  /* Field-group heading (mirrors the global <label> look). */
  .field-label {
    display: block;
    font-weight: 500;
    margin-bottom: 4px;
  }

  /* Per-draft posting progress bar in the list. */
  .draft-progress {
    display: block;
    margin-top: 0.35rem;
    height: 4px;
    background: var(--vk-border);
    border-radius: 999px;
    overflow: hidden;
  }
  .draft-progress-fill {
    display: block;
    height: 100%;
    background: var(--vk-blue);
    border-radius: 999px;
    transition: width 200ms ease;
  }
  .draft-progress-fill.done { background: var(--vk-success); }

  /* Drag-to-reorder the To post queue. */
  .drag-handle {
    cursor: grab;
    color: var(--vk-text-secondary);
    margin-right: 0.3rem;
    user-select: none;
  }
  .drag-handle:active { cursor: grabbing; }
  .card.drop-target {
    outline: 2px dashed var(--vk-blue);
    outline-offset: -2px;
  }

  /* Compact icon-only action buttons in the Draft details header. */
  .icon-only {
    padding-left: 0.45rem;
    padding-right: 0.45rem;
  }
  .icon-only.pinned {
    background: var(--vk-accent);
    border-color: var(--vk-blue);
  }

  /* Image attachment checklist. */
  .dropzone {
    border: 1.5px dashed var(--vk-border-strong);
    border-radius: var(--radius-sm);
    padding: 0.75rem;
    text-align: center;
    color: var(--vk-text-secondary);
    font-size: 0.85rem;
    transition: background 120ms, border-color 120ms, color 120ms;
  }
  .dropzone.drag-over {
    border-color: var(--vk-blue);
    background: var(--vk-accent);
    color: var(--vk-blue);
  }
  .image-list {
    list-style: none;
    margin: 0.25rem 0 0;
    padding: 0;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .image-list li {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.3rem 0.5rem;
    border: 1px solid var(--vk-border);
    border-radius: var(--radius-sm);
  }
  .image-list .img-name {
    flex: 1;
    min-width: 0;
    overflow-wrap: anywhere;
    font-family: 'JetBrains Mono', Consolas, monospace;
    font-size: 0.82rem;
  }
  .img-remove {
    appearance: none;
    border: none;
    background: transparent;
    color: var(--vk-text-secondary);
    cursor: pointer;
    padding: 0.1rem 0.4rem;
    border-radius: 4px;
    font-size: 0.9rem;
  }
  .img-remove:hover { background: var(--vk-danger-bg); color: var(--vk-danger); }
  .image-list li.has-dup { border-color: var(--vk-warning); }
  .img-dup {
    appearance: none;
    border: none;
    background: transparent;
    color: var(--vk-warning);
    cursor: pointer;
    padding: 0.15rem 0.35rem;
    border-radius: 4px;
  }
  .img-dup:hover { background: var(--vk-hover); }

  /* Per-post card header: title on its own row, action buttons below. */
  .post-hdr {
    display: flex;
    flex-direction: column;
    align-items: stretch;
    gap: 0.4rem;
    margin-bottom: 0.6rem;
  }
  .post-title {
    overflow-wrap: anywhere;
  }
  .post-actions {
    justify-content: flex-end;
  }

  /* Per-post character counter. */
  .char-count {
    margin-top: 0.3rem;
    font-size: 0.75rem;
    color: var(--vk-text-secondary);
    font-variant-numeric: tabular-nums;
  }
  .char-count.over {
    color: var(--vk-danger);
    font-weight: 600;
  }
  /* Links in the VK-style preview. */
  .rendered :global(a) {
    color: var(--vk-blue);
    text-decoration: none;
  }
  .rendered :global(a:hover) { text-decoration: underline; }

  /* Target-group marker filter. */
  .marker-filter {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.3rem;
    margin-bottom: 0.5rem;
  }
  .marker-chip.filter {
    appearance: none;
    border: 1px solid var(--vk-border-strong);
    background: transparent;
    color: var(--vk-text-secondary);
    cursor: pointer;
  }
  .marker-chip.filter.on {
    background: var(--vk-blue);
    border-color: var(--vk-blue);
    color: var(--vk-on-primary);
  }

  /* Validation issues panel on the Draft details card. */
  .issues {
    margin: 0 0 0.6rem;
    padding: 0.5rem 0.75rem;
    list-style: none;
    background: var(--vk-banner-err-bg);
    color: var(--vk-banner-err-fg);
    border-radius: var(--radius-sm);
    font-size: 0.85rem;
  }
  .issues li { margin: 0.1rem 0; }

  /* Segmented status filter above the draft list. */
  .seg {
    display: flex;
    gap: 2px;
    margin-bottom: 0.6rem;
    background: var(--vk-hover);
    border-radius: var(--radius-sm);
    padding: 2px;
  }
  .seg-btn {
    flex: 1;
    appearance: none;
    border: none;
    background: transparent;
    color: var(--vk-text-secondary);
    font: inherit;
    font-size: 0.8rem;
    font-weight: 600;
    padding: 0.3rem 0.4rem;
    border-radius: calc(var(--radius-sm) - 2px);
    cursor: pointer;
    white-space: nowrap;
    transition: background 120ms, color 120ms;
  }
  .seg-btn:hover { color: var(--vk-text); }
  .seg-btn.active {
    background: var(--vk-surface);
    color: var(--vk-blue);
    box-shadow: var(--shadow-sm);
  }
</style>

