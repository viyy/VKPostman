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

  const typeOptions: Array<{ v: PlaceholderType; label: string }> = [
    { v: PlaceholderType.Text, label: 'Text' },
    { v: PlaceholderType.VkLink, label: 'VK link (@name)' },
    { v: PlaceholderType.WikiLink, label: 'Wiki link [target|display]' },
    { v: PlaceholderType.Url, label: 'URL' },
    { v: PlaceholderType.TagList, label: 'Tag list' },
  ];

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

  const statusLabel = $derived.by(() => {
    switch (saveStatus) {
      case 'dirty':  return '…';
      case 'saving': return 'Saving…';
      case 'saved':  return '✓ Saved';
      case 'error':  return '⚠ Save failed';
      default:       return '';
    }
  });

  const currentUsages = $derived(editing ? usagesFor(editing.key) : []);
</script>

<div class="editor-layout">
  <aside class="card">
    <div class="card-header">
      <h3 style="margin: 0;">Placeholders</h3>
      <button class="btn btn-primary btn-sm" onclick={addNew}>+ New</button>
    </div>
    {#if !library}
      <p class="muted">Loading…</p>
    {:else if library.length === 0}
      <p class="muted">No placeholders yet. Type <code>{'{{ foo }}'}</code> in a template's Body and one will appear here.</p>
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
        Pick a placeholder on the left, or click <em>+ New</em>.
      </p>
    {:else}
      <div class="card-header">
        <h3 style="margin: 0;">Edit placeholder</h3>
        <div class="row">
          <span class="muted" style="min-width: 5rem; text-align: right;">{statusLabel}</span>
          <button class="btn btn-ghost btn-sm" onclick={close}>Close</button>
        </div>
      </div>

      <div class="stack-lg">
        <div class="stack">
          <label for="p-key">Key (referenced as <code>{'{{ key }}'}</code>)</label>
          <input id="p-key" type="text"
                 style="font-family: 'JetBrains Mono', Consolas, monospace;"
                 bind:value={editing.key} />
        </div>
        <div class="stack">
          <label for="p-display">Display name</label>
          <input id="p-display" type="text" bind:value={editing.displayName} />
        </div>
        <div class="stack">
          <label for="p-type">Type</label>
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
          <label for="p-desc">Description (optional)</label>
          <input id="p-desc" type="text" bind:value={editing.description} />
        </div>
        <div class="stack">
          <label for="p-default">Default value (used when the draft leaves this field empty)</label>
          <input id="p-default" type="text" bind:value={editing.defaultValue} />
        </div>

        <div>
          <h4 style="margin: 1rem 0 0.4rem;">Used by</h4>
          {#if currentUsages.length === 0}
            <p class="muted">(no templates reference this key)</p>
          {:else}
            <ul style="margin: 0; padding-left: 1.25rem;">
              {#each currentUsages as t (t.id)}
                <li>{t.name}</li>
              {/each}
            </ul>
          {/if}
        </div>

        <div>
          <button class="btn btn-danger btn-sm" onclick={() => remove(editing!)}>
            🗑 Delete placeholder
          </button>
        </div>
      </div>
    {/if}
  </section>
</div>
