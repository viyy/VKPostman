<script lang="ts">
  import { liveQuery } from 'dexie';
  import { db, savePlaceholder, deletePlaceholder } from '../lib/db';
  import { extractLibraryPlaceholderKeys } from '../lib/render';
  import {
    PlaceholderType,
    type PlaceholderDefinition,
    type PostTemplate,
  } from '../lib/types';
  import { createAutosave, type AutosaveStatus } from '../lib/autosave';
  import { t } from '../lib/i18n.svelte';
  import { Plus, Trash2 } from '@lucide/svelte';

  const libraryQuery = liveQuery(() => db.placeholders.orderBy('key').toArray());
  const templatesQuery = liveQuery(() => db.templates.toArray());

  let library = $state<PlaceholderDefinition[] | undefined>(undefined);
  let templates = $state<PostTemplate[]>([]);

  $effect(() => {
    const s = libraryQuery.subscribe({ next: (v) => (library = v) });
    return () => s.unsubscribe();
  });
  $effect(() => {
    const s = templatesQuery.subscribe({ next: (v) => (templates = v) });
    return () => s.unsubscribe();
  });

  let editing = $state<PlaceholderDefinition | null>(null);
  let saveStatus = $state<AutosaveStatus>('idle');

  const typeOptions = $derived<Array<{ v: PlaceholderType; label: string }>>([
    { v: PlaceholderType.Text, label: t('Text') },
    { v: PlaceholderType.VkLink, label: t('VK link (@name)') },
    { v: PlaceholderType.WikiLink, label: t('Wiki link [target|display]') },
    { v: PlaceholderType.Url, label: t('URL') },
    { v: PlaceholderType.TagList, label: t('Tag list') },
  ]);

  const autosave = createAutosave<PlaceholderDefinition>({
    get: () => editing,
    save: async (snap) => { await savePlaceholder(snap); },
    // $state.snapshot gives us a plain object that Dexie/structuredClone
    // handle reliably — avoids a class of "proxy can't be cloned" errors.
    snapshot: (v) => $state.snapshot(v) as PlaceholderDefinition,
    delayMs: 500,
    onStatus: (s) => (saveStatus = s),
  });
  $effect(autosave.watch);

  async function edit(d: PlaceholderDefinition) {
    await autosave.flush();
    editing = { ...d };
    // Adopt this record as the baseline so just-opening-it doesn't count as dirty.
    autosave.reset();
  }

  async function close() {
    await autosave.flush();
    editing = null;
  }

  async function addNew() {
    await autosave.flush();
    const now = new Date();
    const def: PlaceholderDefinition = {
      key: `field${(library?.length ?? 0) + 1}`,
      displayName: 'New field',
      type: PlaceholderType.Text,
      createdAt: now,
      updatedAt: now,
    };
    const id = await db.placeholders.add(def);
    await edit({ ...def, id });
  }

  async function remove(d: PlaceholderDefinition) {
    if (!d.id) return;
    const uses = usagesFor(d.key);
    const msg =
      uses.length > 0
        ? `"${d.key}" is referenced by ${uses.length} template(s): ${uses.map((t) => t.name).join(', ')}. ` +
          `Delete anyway? Those templates will render the placeholder as empty.`
        : `Delete placeholder "${d.key}"?`;
    if (!confirm(msg)) return;
    await deletePlaceholder(d.id);
    if (editing?.id === d.id) editing = null;
  }

  function usagesFor(key: string): PostTemplate[] {
    return templates.filter((t) =>
      extractLibraryPlaceholderKeys(t.bodyTemplate).includes(key),
    );
  }

  /** Library entries whose key isn't referenced by any template body. */
  const unusedPlaceholders = $derived.by(() => {
    if (!library) return [] as PlaceholderDefinition[];
    const usedKeys = new Set<string>();
    for (const t of templates) {
      for (const k of extractLibraryPlaceholderKeys(t.bodyTemplate)) usedKeys.add(k);
    }
    return library.filter((d) => !usedKeys.has(d.key));
  });

  async function removeUnused() {
    const orphans = unusedPlaceholders;
    if (orphans.length === 0) return;
    const names = orphans.map((o) => `{{ ${o.key} }}`).join(', ');
    if (!confirm(`Delete ${orphans.length} placeholder(s) not referenced by any template?\n\n${names}`)) {
      return;
    }
    // If the open editor is about to be deleted, close it first.
    const editingDeleted = editing != null && orphans.some((o) => o.id === editing!.id);
    for (const o of orphans) {
      if (o.id != null) await deletePlaceholder(o.id);
    }
    if (editingDeleted) editing = null;
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

  const currentUsages = $derived(editing ? usagesFor(editing.key) : []);
</script>

<div class="editor-layout">
  <aside class="card">
    <div class="card-header">
      <h3 style="margin: 0;">{t('Placeholders')}</h3>
      <button class="btn btn-primary btn-sm" onclick={addNew}><Plus size={15} /> {t('New')}</button>
    </div>
    {#if unusedPlaceholders.length > 0}
      <button
        class="btn btn-ghost btn-sm"
        style="width: 100%; margin-bottom: 8px;"
        onclick={removeUnused}
        title={t('Remove unused ({n})', { n: unusedPlaceholders.length })}
      ><Trash2 size={15} /> {t('Remove unused ({n})', { n: unusedPlaceholders.length })}</button>
    {/if}
    {#if !library}
      <p class="muted">{t('Loading…')}</p>
    {:else if library.length === 0}
      <p class="muted">{t('No placeholders yet. Type {code} in a template’s Body and one will appear here.', { code: '{{ foo }}' })}</p>
    {:else}
      <div class="list">
        {#each library as d (d.id)}
          <button
            class="list-item"
            class:active={editing?.id === d.id}
            onclick={() => edit(d)}
          >
            <strong>{`{{ ${d.key} }}`}</strong>
            <span class="meta">{d.displayName}</span>
          </button>
        {/each}
      </div>
    {/if}
  </aside>

  <section class="card" style="grid-column: span 2;">
    {#if !editing}
      <p class="muted" style="text-align: center; padding: 2rem 0;">
        {t('Pick a placeholder on the left, or click')} <em>+ {t('New')}</em>.
      </p>
    {:else}
      <div class="card-header">
        <h3 style="margin: 0;">{t('Edit placeholder')}</h3>
        <div class="row">
          <span class="muted" style="min-width: 5rem; text-align: right;">{statusLabel}</span>
          <button class="btn btn-ghost btn-sm" onclick={close}>{t('Close')}</button>
        </div>
      </div>

      <div class="stack-lg">
        <div class="stack">
          <label for="p-key">{t('Key (referenced as {code})', { code: '{{ key }}' })}</label>
          <input id="p-key" type="text"
                 style="font-family: 'JetBrains Mono', Consolas, monospace;"
                 bind:value={editing.key} />
        </div>
        <div class="stack">
          <label for="p-display">{t('Display name')}</label>
          <input id="p-display" type="text" bind:value={editing.displayName} />
        </div>
        <div class="stack">
          <label for="p-type">{t('Type')}</label>
          <!--
            value + onchange instead of bind:value — avoids the well-known
            Svelte select-binding race where an initial render without options
            can clobber the bound value with the first available option.
          -->
          <select
            id="p-type"
            value={editing.type}
            onchange={(e) => {
              const raw = (e.currentTarget as HTMLSelectElement).value;
              editing!.type = Number(raw) as PlaceholderType;
            }}
          >
            {#each typeOptions as opt (opt.v)}
              <option value={opt.v}>{opt.label}</option>
            {/each}
          </select>
        </div>
        <div class="stack">
          <label for="p-desc">{t('Description (optional)')}</label>
          <input id="p-desc" type="text" bind:value={editing.description} />
        </div>
        <div class="stack">
          <label for="p-default">{t('Default value (used when the draft leaves this field empty)')}</label>
          <input id="p-default" type="text" bind:value={editing.defaultValue} />
        </div>

        <div>
          <h4 style="margin: 1rem 0 0.4rem;">{t('Used by')}</h4>
          {#if currentUsages.length === 0}
            <p class="muted">{t('(no templates reference this key)')}</p>
          {:else}
            <ul style="margin: 0; padding-left: 1.25rem;">
              {#each currentUsages as tpl (tpl.id)}
                <li>{tpl.name}</li>
              {/each}
            </ul>
          {/if}
        </div>

        <div>
          <button class="btn btn-danger btn-sm" onclick={() => remove(editing!)}>
            <Trash2 size={15} /> {t('Delete placeholder')}
          </button>
        </div>
      </div>
    {/if}
  </section>
</div>
