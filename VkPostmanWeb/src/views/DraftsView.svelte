<script lang="ts">
  import { liveQuery } from 'dexie';
  import { db, createDraft, saveDraft, deleteDraft } from '../lib/db';
  import {
    isDraftReady,
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

  /** Collapsed state for the Target groups block (persists across reloads). */
  let groupsCollapsed = $state(localStorage.getItem('vkp.draftGroupsCollapsed') === '1');
  $effect(() => {
    localStorage.setItem('vkp.draftGroupsCollapsed', groupsCollapsed ? '1' : '0');
  });

  let saveStatus = $state<AutosaveStatus>('idle');

  // ---- Live data -----------------------------------------------------------
  const draftsQuery    = liveQuery(() => db.drafts.orderBy('updatedAt').reverse().toArray());
  const groupsQuery    = liveQuery(() => db.groups.orderBy('displayName').toArray());
  const templatesQuery = liveQuery(() => db.templates.toArray());
  const libraryQuery   = liveQuery(() => db.placeholders.toArray());

  let drafts    = $state<PostDraft[] | undefined>(undefined);
  let groups    = $state<TargetGroup[]>([]);
  let templates = $state<PostTemplate[]>([]);
  let library   = $state<PlaceholderDefinition[]>([]);

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

  const selectedGroups = $derived(
    draft ? groups.filter((g) => draft!.targetGroupIds.includes(g.id!)) : []
  );

  const placeholders = $derived(
    unionedPlaceholders(selectedGroups, templatesById, libraryByKey)
  );

  const ready = $derived(draft ? isDraftReady(draft, selectedGroups) : false);

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
    if (!confirm('Delete this draft?')) return;
    await deleteDraft(draft.id);
    currentId = null;
  }

  function markPosted(groupId: number) {
    if (!draft) return;
    if (!draft.postedGroupIds.includes(groupId)) {
      draft.postedGroupIds = [...draft.postedGroupIds, groupId];
    }
  }

  function unmarkPosted(groupId: number) {
    if (!draft) return;
    draft.postedGroupIds = draft.postedGroupIds.filter((id) => id !== groupId);
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
      <div class="list">
        {#each drafts as d (d.id)}
          <button
            class="list-item"
            class:active={currentId === d.id}
            onclick={() => d.id != null && selectDraft(d.id)}
          >
            <strong>{d.title}</strong>
            <span class="meta">{new Date(d.updatedAt).toLocaleString()}</span>
          </button>
        {/each}
      </div>
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
          <h3 style="margin: 0;">Draft details</h3>
          <div class="row">
            <span class="pill" style={ready ? '' : 'background:#eef0f3; color:var(--vk-text-secondary);'}>
              {ready ? 'ready to copy' : 'incomplete'}
            </span>
            <span class="muted" style="min-width: 5rem; text-align: right;">{statusLabel}</span>
            <button class="btn btn-danger btn-sm" onclick={remove}>🗑 Delete</button>
          </div>
        </div>

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
          </div>
        </div>
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
                <label>
                  {def?.displayName ?? u.key}
                  <span class="muted">&nbsp;({typeLabel(def?.type ?? PlaceholderType.Text)})</span>
                </label>

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
      <h3 style="margin: 0;">To post</h3>
      {#if postedRenders.length > 0}
        <span class="muted">{postedRenders.length} posted</span>
      {/if}
    </div>

    {#if renders.length === 0}
      <p class="muted">Pick one or more target groups on the left to see rendered posts.</p>
    {:else if activeRenders.length === 0}
      <p class="muted">All selected groups are marked posted. 🎉</p>
    {:else}
      {#each activeRenders as r (r.group.id)}
        <div class="card">
          <div class="card-header">
            <strong>{r.group.displayName}</strong>
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

    <!-- Posted block -->
    {#if postedRenders.length > 0}
      <div class="card-header" style="padding-left: 4px; margin-top: 1rem;">
        <h3 style="margin: 0;">✓ Posted</h3>
      </div>
      {#each postedRenders as r (r.group.id)}
        <div class="card posted-card">
          <div class="card-header">
            <strong>{r.group.displayName}</strong>
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
</style>

