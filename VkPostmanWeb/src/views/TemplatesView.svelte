<script lang="ts">
  import { liveQuery } from 'dexie';
  import { db, createTemplate, saveTemplate, deleteTemplate, ensurePlaceholder, saveGroup } from '../lib/db';
  import {
    type PlaceholderDefinition,
    type PostTemplate,
    type TargetGroup,
  } from '../lib/types';
  import { createAutosave, type AutosaveStatus } from '../lib/autosave';
  import {
    BUILT_IN_PLACEHOLDERS,
    extractLibraryPlaceholderKeys,
    isBuiltInPlaceholder,
    renderTemplatePreview,
  } from '../lib/render';
  import { nav } from '../lib/nav.svelte';
  import { knownTagsQuery } from '../lib/tags';
  import { undo } from '../lib/undo.svelte';
  import { t } from '../lib/i18n.svelte';
  import TagSuggestions from './TagSuggestions.svelte';
  import SearchSelect from './SearchSelect.svelte';
  import { Plus, Trash2, ExternalLink, Users } from '@lucide/svelte';
  import { tick } from 'svelte';

  const templatesQuery = liveQuery(() => db.templates.orderBy('updatedAt').reverse().toArray());
  const libraryQuery   = liveQuery(() => db.placeholders.orderBy('key').toArray());
  const groupsQuery    = liveQuery(() => db.groups.toArray());
  const tagsQuery      = knownTagsQuery();

  let templates = $state<PostTemplate[] | undefined>(undefined);
  let library = $state<PlaceholderDefinition[]>([]);
  let groups = $state<TargetGroup[]>([]);
  let knownTags = $state<string[]>([]);

  $effect(() => {
    const s = templatesQuery.subscribe({ next: (v) => (templates = v) });
    return () => s.unsubscribe();
  });
  $effect(() => {
    const s = libraryQuery.subscribe({ next: (v) => (library = v) });
    return () => s.unsubscribe();
  });
  $effect(() => {
    const s = groupsQuery.subscribe({ next: (v) => (groups = v) });
    return () => s.unsubscribe();
  });
  $effect(() => {
    const s = tagsQuery.subscribe({ next: (v) => (knownTags = v) });
    return () => s.unsubscribe();
  });

  const libraryByKey = $derived(new Map(library.map((d) => [d.key, d])));

  /** Live preview of the body filled with sample values. */
  const preview = $derived.by(() =>
    editing ? renderTemplatePreview(editing.bodyTemplate ?? '', libraryByKey, editing.defaultThemeTags) : '',
  );

  function addTag(tag: string) {
    const cur = defaultTagsInput.trim();
    defaultTagsInput = cur ? `${cur} ${tag}` : tag;
  }

  // ---- Search (by template name, or a using group's name / alias) ----------
  let search = $state('');

  const groupsByTemplateId = $derived.by(() => {
    const m = new Map<number, TargetGroup[]>();
    for (const g of groups) {
      if (g.postTemplateId == null) continue;
      const arr = m.get(g.postTemplateId) ?? [];
      arr.push(g);
      m.set(g.postTemplateId, arr);
    }
    return m;
  });

  const filteredTemplates = $derived.by(() => {
    const list = templates ?? [];
    const q = search.trim().toLowerCase();
    if (!q) return list;
    return list.filter((t) => {
      if (t.name.toLowerCase().includes(q)) return true;
      if (t.description?.toLowerCase().includes(q)) return true;
      const using = t.id != null ? groupsByTemplateId.get(t.id) ?? [] : [];
      return using.some(
        (g) =>
          g.displayName.toLowerCase().includes(q) ||
          g.screenName.toLowerCase().includes(q),
      );
    });
  });

  // Honour a "jump to this template" request from another tab (e.g. the
  // Drafts page's clickable template link). Re-runs when templates load,
  // so it works even if the request lands before the liveQuery resolves.
  $effect(() => {
    const reqId = nav.requestedTemplateId;
    if (reqId == null) return;
    const list = templates;
    if (!list) return; // wait for the liveQuery
    const t = list.find((x) => x.id === reqId);
    if (t) {
      nav.requestedTemplateId = null;
      void edit(t);
    }
  });

  let editing = $state<PostTemplate | null>(null);
  let defaultTagsInput = $state('');
  let saveStatus = $state<AutosaveStatus>('idle');

  // Groups assigned to the template currently being edited.
  const usingGroups = $derived(
    editing?.id != null ? groupsByTemplateId.get(editing.id) ?? [] : [],
  );
  // Groups NOT yet using this template — candidates to link.
  const unlinkedGroups = $derived(
    editing?.id != null ? groups.filter((g) => g.postTemplateId !== editing!.id) : [],
  );

  /** Assign this template to a group (overwrites that group's template). */
  async function linkGroup(gid: number | undefined) {
    if (gid == null || editing?.id == null) return;
    const g = await db.groups.get(gid);
    if (!g) return;
    g.postTemplateId = editing.id;
    await saveGroup(g);
  }

  // Keep the tag-input in sync with the editing payload so autosave fires.
  $effect(() => {
    if (!editing) return;
    editing.defaultThemeTags = defaultTagsInput
      .split(/\s+/)
      .map((t) => t.replace(/^#+/, '').trim())
      .filter(Boolean);
  });

  const autosave = createAutosave<PostTemplate>({
    get: () => editing,
    save: async (snap) => { await saveTemplate(snap); },
    snapshot: (v) => $state.snapshot(v) as PostTemplate,
    delayMs: 500,
    onStatus: (s) => (saveStatus = s),
  });
  $effect(autosave.watch);

  async function edit(t: PostTemplate) {
    await autosave.flush();
    editing = {
      ...t,
      defaultThemeTags: [...t.defaultThemeTags],
    };
    defaultTagsInput = t.defaultThemeTags.join(' ');
    autosave.reset();
  }

  async function close() {
    await autosave.flush();
    editing = null;
  }

  async function addNew() {
    await autosave.flush();
    const id = await createTemplate();
    const t = await db.templates.get(id);
    if (t) await edit(t);
  }

  async function remove(t: PostTemplate) {
    if (!t.id) return;
    const snap = $state.snapshot(t) as PostTemplate;
    await deleteTemplate(t.id);
    if (editing?.id === t.id) editing = null;
    undo.offer(`Deleted template “${snap.name}”`, async () => { await db.templates.put(snap); });
  }

  // ---- Body editor state (chip toolbar + autocomplete + auto-sync) ---------

  let bodyTextarea: HTMLTextAreaElement | undefined = $state();
  let caretPos = $state(0);

  const autocompleteQuery = $derived.by<string | null>(() => {
    if (!editing) return null;
    const body = editing.bodyTemplate ?? '';
    const left = body.slice(0, caretPos);
    const openIdx = left.lastIndexOf('{{');
    if (openIdx < 0) return null;
    const between = left.slice(openIdx + 2);
    if (between.includes('}}')) return null;
    return between.replace(/^\s*/, '');
  });

  const suggestedKeys = $derived.by<string[]>(() => {
    const libKeys = library.map((d) => d.key);
    const all = [...libKeys, ...BUILT_IN_PLACEHOLDERS];
    const q = autocompleteQuery;
    if (q == null) return all;
    const lower = q.toLowerCase();
    return all.filter((k) => k.toLowerCase().startsWith(lower));
  });

  /** The read-only "placeholders used" list below the Body. */
  const usedPlaceholders = $derived.by(() => {
    if (!editing) return [] as Array<{ key: string; displayName: string; type: string }>;
    const libByKey = new Map(library.map((d) => [d.key, d]));
    return extractLibraryPlaceholderKeys(editing.bodyTemplate).map((key) => {
      const def = libByKey.get(key);
      return {
        key,
        displayName: def?.displayName ?? '(pending…)',
        type: def ? typeLabel(def.type) : 'text',
      };
    });
  });

  /**
   * When a library-bound key appears in the body and isn't in the library yet,
   * create it. Debounced ~1.2s after the last keystroke so half-typed keys
   * ({{ group_ }}, {{ group_h }}, …) don't each get persisted — only the
   * settled key does. ensurePlaceholder() is idempotent (checks the DB first),
   * so this stays safe even if the timer fires twice.
   */
  const ENSURE_DELAY_MS = 1200;
  let _ensureTimer: ReturnType<typeof setTimeout> | undefined;
  $effect(() => {
    if (!editing) return;
    const body = editing.bodyTemplate; // dependency
    clearTimeout(_ensureTimer);
    _ensureTimer = setTimeout(() => {
      const existing = new Set(library.map((d) => d.key));
      for (const key of extractLibraryPlaceholderKeys(body)) {
        if (!existing.has(key)) void ensurePlaceholder(key);
      }
    }, ENSURE_DELAY_MS);
    return () => clearTimeout(_ensureTimer);
  });

  async function insertPlaceholderAtCaret(key: string) {
    if (!editing || !bodyTextarea) return;
    const body = editing.bodyTemplate ?? '';
    const start = bodyTextarea.selectionStart ?? body.length;
    const end = bodyTextarea.selectionEnd ?? start;

    let newBody: string;
    let newCaret: number;

    if (autocompleteQuery != null) {
      const left = body.slice(0, start);
      const openIdx = left.lastIndexOf('{{');
      const replaced = `{{ ${key} }}`;
      newBody = body.slice(0, openIdx) + replaced + body.slice(end);
      newCaret = openIdx + replaced.length;
    } else {
      const token = `{{ ${key} }}`;
      newBody = body.slice(0, start) + token + body.slice(end);
      newCaret = start + token.length;
    }

    editing.bodyTemplate = newBody;
    await tick();
    bodyTextarea.focus();
    bodyTextarea.setSelectionRange(newCaret, newCaret);
    caretPos = newCaret;
  }

  function syncCaret() {
    if (!bodyTextarea) return;
    caretPos = bodyTextarea.selectionStart ?? 0;
  }

  const statusLabel = $derived.by(() => {
    switch (saveStatus) {
      case 'dirty':  return '…';
      case 'saving': return t('Saving…');
      case 'saved':  return t('✓ Saved');
      case 'error':  return t('⚠ Save failed');
      default:       return '';
    }
  });

  function typeLabel(type: number): string {
    switch (type) {
      case 1:  return t('VK link');
      case 4:  return t('wiki link');
      case 2:  return t('URL');
      case 3:  return t('tags');
      default: return t('text');
    }
  }
</script>

<div class="editor-layout">
  <aside class="card">
    <div class="card-header">
      <h3 style="margin: 0;">Templates</h3>
      <button class="btn btn-primary btn-sm" onclick={addNew}><Plus size={15} /> New</button>
    </div>
    {#if !templates}
      <p class="muted">{t('Loading…')}</p>
    {:else if templates.length === 0}
      <p class="muted">{t('No templates yet.')}</p>
    {:else}
      <input
        type="text"
        class="search-input"
        placeholder={t('Search name, group, alias…')}
        bind:value={search}
        aria-label="Search templates"
      />
      {#if filteredTemplates.length === 0}
        <p class="muted">{t('No matches for “{q}”.', { q: search })}</p>
      {:else}
        <div class="list">
          {#each filteredTemplates as tpl (tpl.id)}
            <button
              class="list-item"
              class:active={editing?.id === tpl.id}
              onclick={() => edit(tpl)}
            >
              <strong>{tpl.name}</strong>
              <span class="meta">{new Date(tpl.updatedAt).toLocaleString()}</span>
            </button>
          {/each}
        </div>
      {/if}
    {/if}
  </aside>

  <section class="card" style="grid-column: span 2;">
    {#if !editing}
      <p class="muted" style="text-align: center; padding: 2rem 0;">
        {t('Pick a template on the left, or click')} <em>+ {t('New')}</em>.
      </p>
    {:else}
      <div class="card-header">
        <h3 style="margin: 0;">{t('Edit template')}</h3>
        <div class="row">
          <span class="muted" style="min-width: 5rem; text-align: right;">{statusLabel}</span>
          <button class="btn btn-ghost btn-sm" onclick={close}>{t('Close')}</button>
        </div>
      </div>

      <div class="stack-lg">
        <div class="stack">
          <label for="t-name">{t('Name')}</label>
          <input id="t-name" type="text" bind:value={editing.name} />
        </div>
        <div class="stack">
          <label for="t-desc">{t('Description')}</label>
          <input id="t-desc" type="text" bind:value={editing.description} />
        </div>
        <div class="stack">
          <label for="t-body">{t('Body')}</label>
          <textarea
            id="t-body"
            bind:this={bodyTextarea}
            style="font-family: 'JetBrains Mono', Consolas, monospace; min-height: 150px;"
            bind:value={editing.bodyTemplate}
            oninput={syncCaret}
            onclick={syncCaret}
            onkeyup={syncCaret}
            onselect={syncCaret}
          ></textarea>

          <!-- Chip toolbar — quick insert + autocomplete -->
          <div class="placeholder-chiprow">
            {#if autocompleteQuery != null}
              <span class="muted" style="font-size: 0.75rem;">
                Completing <code>{`{{ ${autocompleteQuery}…`}</code>
              </span>
            {:else}
              <span class="muted" style="font-size: 0.75rem;">{t('Insert:')}</span>
            {/if}
            {#each suggestedKeys as key (key)}
              <button
                type="button"
                class="chip {isBuiltInPlaceholder(key) ? 'chip-global' : 'chip-user'}"
                onclick={() => insertPlaceholderAtCaret(key)}
              >{`{{ ${key} }}`}</button>
            {/each}
            {#if suggestedKeys.length === 0}
              <span class="muted" style="font-size: 0.78rem;">
                {t('No matches. Type the name — a placeholder row will be added for you.')}
              </span>
            {/if}
          </div>

          <span class="muted">
            {t('Scriban-like {code} syntax. New keys auto-appear on the Placeholders tab.', { code: '{{ placeholder }}' })}
            {t('Optional blocks: {if} and {unless} — shown only when the value is filled.', { if: '{{#if key}}…{{else}}…{{/if}}', unless: '{{#unless key}}…{{/unless}}' })}
          </span>
        </div>

        <div class="stack">
          <div class="field-label">{t('Live preview')} <span class="muted">{t('(sample values)')}</span></div>
          <div class="rendered">{preview}</div>
        </div>

        <div class="stack">
          <label for="t-dtags">{t('Default theme tags')}</label>
          <input id="t-dtags" type="text" bind:value={defaultTagsInput} />
          <TagSuggestions tags={knownTags} current={defaultTagsInput} onpick={addTag} />
        </div>

        <div>
          <h4 style="margin: 1rem 0 0.4rem;">{t('Placeholders used')}</h4>
          <p class="muted" style="margin: 0 0 0.4rem;">
            {t('Derived from the Body. Edit definitions on the Placeholders tab — changes propagate to every template using that key.')}
          </p>
          {#if usedPlaceholders.length === 0}
            <p class="muted">
              {t('None yet. Type {code} in the Body to add one.', { code: '{{ my_key }}' })}
            </p>
          {:else}
            <div class="stack" style="gap: 0.25rem;">
              {#each usedPlaceholders as u (u.key)}
                <div class="used-row">
                  <code>{`{{ ${u.key} }}`}</code>
                  <span>{u.displayName}</span>
                  <span class="muted">{u.type}</span>
                </div>
              {/each}
            </div>
          {/if}
        </div>

        <div>
          <h4 style="margin: 1rem 0 0.4rem;">{t('Used by groups')}</h4>
          {#if editing.id != null}
            <div class="row" style="gap: 0.4rem; margin-bottom: 0.5rem;">
              <div class="grow">
                <SearchSelect
                  value={undefined}
                  items={unlinkedGroups.map((g) => ({ id: g.id, label: g.displayName, sub: `@${g.screenName}` }))}
                  allowNone={false}
                  triggerPlaceholder={t('Link a group to this template…')}
                  searchPlaceholder={t('Search groups…')}
                  onchange={linkGroup}
                />
              </div>
            </div>
          {/if}
          {#if usingGroups.length === 0}
            <p class="muted">{t('No groups use this template yet. Pick one above to link it.')}</p>
          {:else}
            <div class="stack" style="gap: 0.25rem;">
              {#each usingGroups as g (g.id)}
                <div class="used-row">
                  <span><Users size={14} class="inline-ico" /> {g.displayName}</span>
                  <span class="muted">@{g.screenName}</span>
                  <button
                    type="button"
                    class="btn btn-outline btn-sm"
                    title={t('Open this group')}
                    onclick={() => nav.openGroup(g.id!)}
                  ><ExternalLink size={14} /> {t('Open')}</button>
                </div>
              {/each}
            </div>
          {/if}
        </div>

        {#if editing.id != null}
          <div style="margin-top: 0.5rem;">
            <button class="btn btn-danger btn-sm" onclick={() => remove(editing!)}>
              <Trash2 size={15} /> {t('Delete template')}
            </button>
          </div>
        {/if}
      </div>
    {/if}
  </section>
</div>

<style>
  .used-row {
    display: grid;
    grid-template-columns: auto 1fr auto;
    gap: 0.6rem;
    padding: 0.35rem 0.5rem;
    border: 1px solid var(--vk-border);
    border-radius: 6px;
    align-items: center;
  }
</style>
