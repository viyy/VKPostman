<script lang="ts">
  import { liveQuery } from 'dexie';
  import { db, createDraft, duplicateDraft, saveDraft, deleteDraft } from '../lib/db';
  import {
    packWikiLink,
    renderDraftForGroup,
    splitWikiLink,
    unionedPlaceholders,
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
  import TagSuggestions from './TagSuggestions.svelte';

  /** Collapsed state for the collapsible blocks (persists across reloads). */
  let groupsCollapsed  = $state(localStorage.getItem('vkp.draftGroupsCollapsed') === '1');
  let detailsCollapsed = $state(localStorage.getItem('vkp.draftDetailsCollapsed') === '1');
  let toPostCollapsed  = $state(localStorage.getItem('vkp.draftToPostCollapsed') === '1');
  let postedCollapsed  = $state(localStorage.getItem('vkp.draftPostedCollapsed') === '1');
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

  let saveStatus = $state<AutosaveStatus>('idle');

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
      case 'saving': return 'Saving…';
      case 'saved':  return '✓ Saved';
      case 'error':  return '⚠ Save failed';
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
    if (selectedGroups.length === 0) issues.push('No target groups selected.');
    for (const g of selectedGroups) {
      if (g.postTemplateId == null) issues.push(`${g.displayName}: no template assigned.`);
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
        issues.push(`Missing value: ${def?.displayName ?? u.key} (${u.usedByGroups.join(', ')})`);
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
      <h3 style="margin: 0;">Drafts</h3>
      <button class="btn btn-primary btn-sm" onclick={newDraft}>+ New</button>
    </div>

    {#if !drafts}
      <p class="muted">Loading…</p>
    {:else if drafts.length === 0}
      <p class="muted">No drafts yet.</p>
    {:else}
      <input
        type="text"
        class="search-input"
        placeholder="Search title, text, group…"
        bind:value={search}
        aria-label="Search drafts"
      />
      <div class="seg" role="tablist" aria-label="Filter by status">
        <button
          class="seg-btn" class:active={statusFilter === 'all'}
          onclick={() => (statusFilter = 'all')}
        >All</button>
        <button
          class="seg-btn" class:active={statusFilter === 'active'}
          onclick={() => (statusFilter = 'active')}
        >Active</button>
        <button
          class="seg-btn" class:active={statusFilter === 'posted'}
          onclick={() => (statusFilter = 'posted')}
        >Posted</button>
      </div>

      {#if filteredDrafts.length === 0}
        <p class="muted">No drafts match the current filter.</p>
      {:else}
        <div class="list">
          {#each filteredDrafts as d (d.id)}
            <button
              class="list-item"
              class:active={currentId === d.id}
              onclick={() => d.id != null && selectDraft(d.id)}
            >
              <strong>
                {#if d.pinned}<span title="Pinned">📌 </span>{/if}{#if isFullyPosted(d)}<span title="All target groups posted">✓ </span>{/if}{d.title}
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
          Pick a draft on the left, or click <em>+ New</em>.
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
            <span class="collapse-chevron" class:collapsed={detailsCollapsed}>▾</span>
            <h3 style="margin: 0; white-space: nowrap;">Draft details</h3>
          </button>
          <div class="row" style="flex-wrap: nowrap;">
            <span class="pill" style={ready ? '' : 'background:var(--vk-hover); color:var(--vk-text-secondary);'}>
              {ready ? 'ready' : 'incomplete'}
            </span>
            <span class="muted" style="text-align: right;">{statusLabel}</span>
            <button
              class="btn btn-ghost btn-sm icon-only"
              class:pinned={draft.pinned}
              title={draft.pinned ? 'Unpin' : 'Pin to top'}
              aria-label={draft.pinned ? 'Unpin' : 'Pin to top'}
              onclick={togglePin}
            >📌</button>
            <button
              class="btn btn-ghost btn-sm icon-only"
              title="Duplicate"
              aria-label="Duplicate draft"
              onclick={duplicate}
            >⧉</button>
            <button
              class="btn btn-danger btn-sm icon-only"
              title="Delete"
              aria-label="Delete draft"
              onclick={remove}
            >🗑</button>
          </div>
        </div>

        {#if !ready && validationIssues.length > 0}
          <ul class="issues">
            {#each validationIssues as issue (issue)}
              <li>⚠ {issue}</li>
            {/each}
          </ul>
        {/if}

        {#if !detailsCollapsed}
        <div class="stack-lg">
          <div class="stack">
            <label for="d-title">Title</label>
            <input id="d-title" type="text" bind:value={draft.title} />
          </div>
          <div class="stack">
            <label for="d-common">Common text</label>
            <textarea
              id="d-common"
              bind:value={draft.commonText}
              placeholder={'Rendered via {{ common_text }} in every template'}
            ></textarea>
          </div>
          <div class="stack">
            <label for="d-tags">Theme tags (common to all groups)</label>
            <input id="d-tags" type="text" bind:value={themeTagsInput} />
            <TagSuggestions tags={knownTags} current={themeTagsInput} onpick={addTag} />
          </div>

          <div class="stack">
            <div class="field-label">
              Images to attach
              <span class="muted">&nbsp;(filenames/paths — a manual checklist, files aren't stored)</span>
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
              Drop image files here to add their names
            </div>
            <div class="row" style="gap: 0.4rem;">
              <input
                type="text"
                class="grow"
                placeholder="…or type a filename/path and press Enter"
                bind:value={imageNoteInput}
                onkeydown={(e) => { if (e.key === 'Enter') { e.preventDefault(); addImageNoteFromInput(); } }}
              />
              <button class="btn btn-outline btn-sm" onclick={addImageNoteFromInput}>+ Add</button>
            </div>
            {#if (draft.imageNotes ?? []).length > 0}
              <ul class="image-list">
                {#each draft.imageNotes ?? [] as name (name)}
                  <li>
                    <span class="img-name">🖼 {name}</span>
                    <button
                      class="img-remove"
                      aria-label={`Remove ${name}`}
                      onclick={() => removeImageNote(name)}
                    >✕</button>
                  </li>
                {/each}
              </ul>
            {/if}
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
          <span class="collapse-chevron" class:collapsed={groupsCollapsed}>▾</span>
          <h3 style="margin: 0;">Target groups</h3>
          <span class="muted" style="margin-left: auto;">
            {draft.targetGroupIds.length} of {groups.length} selected
          </span>
        </button>
        {#if !groupsCollapsed}
          {#if groups.length === 0}
            <p class="muted">
              No groups yet — add some on the <strong>Groups</strong> tab.
            </p>
          {:else}
            <div>
              {#each groups as g (g.id)}
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
                        template:
                        <button
                          type="button"
                          class="link-btn"
                          onclick={(e) => { e.preventDefault(); nav.openTemplate(g.postTemplateId!); }}
                        >{templatesById.get(g.postTemplateId!)?.name ?? '(deleted)'}</button>
                      {:else}
                        <span class="warn">⚠ no template assigned</span>
                      {/if}
                    </div>
                  </div>
                </label>
              {/each}
            </div>
          {/if}
        {/if}
      </div>

      <!-- Placeholders (union) -->
      {#if placeholders.length > 0}
        <div class="card">
          <div class="card-header">
            <h3 style="margin: 0;">Placeholders</h3>
            <span class="muted">union across selected groups' templates</span>
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

                <span class="muted">used by: {u.usedByGroups.join(', ')}</span>
              </div>
            {/each}
          </div>
        </div>
      {/if}
    {/if}
  </section>

  <!-- ==== Per-group output ==== -->
  <section>
    <div class="card-header" style="padding-left: 4px;">
      <button
        type="button"
        class="collapse-header"
        style="width: auto; margin-bottom: 0;"
        aria-expanded={!toPostCollapsed}
        onclick={() => (toPostCollapsed = !toPostCollapsed)}
      >
        <span class="collapse-chevron" class:collapsed={toPostCollapsed}>▾</span>
        <h3 style="margin: 0;">To post</h3>
      </button>
      <div class="row">
        {#if postedRenders.length > 0}
          <span class="muted">{postedRenders.length} posted</span>
        {/if}
        <button
          class="btn btn-primary btn-sm"
          disabled={activeRenders.length === 0}
          title="Copy the next unposted group's text and open its vk.com page"
          onclick={copyNextAndOpen}
        >📋 Copy next &amp; open</button>
      </div>
    </div>

    {#if postedSummary && postedSummary.total > 0}
      <p class="muted" style="margin: -0.2rem 0 0.6rem; padding-left: 4px;">
        Posted to {postedSummary.posted}/{postedSummary.total} groups{#if postedSummary.last} · last on {postedSummary.last.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })}{/if}
      </p>
    {/if}

    {#if !toPostCollapsed}
      {#if renders.length === 0}
        <p class="muted">Pick one or more target groups on the left to see rendered posts.</p>
      {:else if activeRenders.length === 0}
        <p class="muted">All selected groups are marked posted. 🎉</p>
      {:else}
        {#each activeRenders as r (r.group.id)}
          <div
            class="card"
            class:drop-target={dragGroupId != null && dragGroupId !== r.group.id}
            ondragover={(e) => { if (dragGroupId != null) e.preventDefault(); }}
            ondrop={() => reorderTo(r.group.id!)}
            role="listitem"
          >
            <div class="card-header">
              <strong>
                <span
                  class="drag-handle"
                  draggable="true"
                  role="button"
                  tabindex="-1"
                  ondragstart={() => (dragGroupId = r.group.id!)}
                  ondragend={() => (dragGroupId = null)}
                  title="Drag to reorder the posting queue"
                  aria-label="Drag to reorder"
                >⠿</span>
                {r.group.displayName}
              </strong>
              <div class="row">
                <button class="btn btn-outline btn-sm" onclick={() => copyText(r.text)}>📋 Copy</button>
                <button class="btn btn-outline btn-sm" onclick={() => openGroup(r.group)}>🌐 Open vk.com</button>
                <button class="btn btn-primary btn-sm" onclick={() => markPosted(r.group.id!)}>✓ Posted</button>
              </div>
            </div>
            <div class="rendered">{r.text}</div>
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
        <span class="collapse-chevron" class:collapsed={postedCollapsed}>▾</span>
        <h3 style="margin: 0;">✓ Posted</h3>
        <span class="muted" style="margin-left: auto;">{postedRenders.length}</span>
      </button>
      {#if !postedCollapsed}
        {#each postedRenders as r (r.group.id)}
          <div class="card posted-card">
            <div class="card-header">
              <strong>
                {r.group.displayName}
                {#if postedDateLabel(r.group.id!)}
                  <span class="muted" style="font-weight: 400;">· posted {postedDateLabel(r.group.id!)}</span>
                {/if}
              </strong>
              <div class="row">
                <button class="btn btn-outline btn-sm" onclick={() => copyText(r.text)}>📋 Copy</button>
                <button class="btn btn-outline btn-sm" onclick={() => openGroup(r.group)}>🌐 Open vk.com</button>
                <button class="btn btn-ghost btn-sm" onclick={() => unmarkPosted(r.group.id!)}>↩ Unmark</button>
              </div>
            </div>
            <div class="rendered">{r.text}</div>
          </div>
        {/each}
      {/if}
    {/if}
  </section>
</div>

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

