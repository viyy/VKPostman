<script lang="ts">
  import { liveQuery } from 'dexie';
  import { db, createTemplate, saveTemplate, deleteTemplate } from '../lib/db';
  import { PlaceholderType, type PlaceholderDefinition, type PostTemplate } from '../lib/types';

  const templatesQuery = liveQuery(() => db.templates.orderBy('updatedAt').reverse().toArray());

  let templates = $state<PostTemplate[] | undefined>(undefined);
  $effect(() => {
    const sub = templatesQuery.subscribe({ next: (v) => (templates = v) });
    return () => sub.unsubscribe();
  });

  let editing = $state<PostTemplate | null>(null);
  let defaultTagsInput = $state('');

  const typeOptions: Array<{ v: PlaceholderType; label: string }> = [
    { v: PlaceholderType.Text, label: 'Text' },
    { v: PlaceholderType.VkLink, label: 'VK link (@name)' },
    { v: PlaceholderType.WikiLink, label: 'Wiki link [target|display]' },
    { v: PlaceholderType.Url, label: 'URL' },
    { v: PlaceholderType.TagList, label: 'Tag list' },
  ];

  function edit(t: PostTemplate) {
    // Deep clone — we don't want to mutate the live-query row while editing.
    editing = {
      ...t,
      defaultThemeTags: [...t.defaultThemeTags],
      placeholderSchema: t.placeholderSchema.map((p) => ({ ...p })),
    };
    defaultTagsInput = t.defaultThemeTags.join(' ');
  }

  function cancel() {
    editing = null;
  }

  async function addNew() {
    const id = await createTemplate();
    const t = await db.templates.get(id);
    if (t) edit(t);
  }

  function addPlaceholder() {
    if (!editing) return;
    editing.placeholderSchema.push({
      key: `field${editing.placeholderSchema.length + 1}`,
      displayName: 'New field',
      isRequired: false,
      type: PlaceholderType.Text,
    });
    editing.placeholderSchema = editing.placeholderSchema; // reactivity
  }

  function removePlaceholder(p: PlaceholderDefinition) {
    if (!editing) return;
    editing.placeholderSchema = editing.placeholderSchema.filter((x) => x !== p);
  }

  async function save() {
    if (!editing) return;
    editing.defaultThemeTags = defaultTagsInput
      .split(/\s+/)
      .map((t) => t.replace(/^#+/, '').trim())
      .filter(Boolean);
    await saveTemplate($state.snapshot(editing) as typeof editing);
    editing = null;
  }

  async function remove(t: PostTemplate) {
    if (!t.id) return;
    if (!confirm(`Delete template "${t.name}"?`)) return;
    await deleteTemplate(t.id);
    if (editing?.id === t.id) editing = null;
  }
</script>

<div class="editor-layout">
  <aside class="card">
    <div class="card-header">
      <h3 style="margin: 0;">Templates</h3>
      <button class="btn btn-primary btn-sm" onclick={addNew}>+ New</button>
    </div>
    {#if !templates}
      <p class="muted">Loading…</p>
    {:else if templates.length === 0}
      <p class="muted">No templates yet.</p>
    {:else}
      <div class="list">
        {#each templates as t (t.id)}
          <button
            class="list-item"
            class:active={editing?.id === t.id}
            onclick={() => edit(t)}
          >
            <strong>{t.name}</strong>
            <span class="meta">
              {new Date(t.updatedAt).toLocaleString()}
            </span>
          </button>
        {/each}
      </div>
    {/if}
  </aside>

  <section class="card" style="grid-column: span 2;">
    {#if !editing}
      <p class="muted" style="text-align: center; padding: 2rem 0;">
        Pick a template on the left, or click <em>+ New</em>.
      </p>
    {:else}
      <div class="card-header">
        <h3 style="margin: 0;">Edit template</h3>
        <div class="row">
          <button class="btn btn-primary btn-sm" onclick={save}>💾 Save</button>
          <button class="btn btn-ghost btn-sm" onclick={cancel}>Cancel</button>
        </div>
      </div>

      <div class="stack-lg">
        <div class="stack">
          <label for="t-name">Name</label>
          <input id="t-name" type="text" bind:value={editing.name} />
        </div>
        <div class="stack">
          <label for="t-desc">Description</label>
          <input id="t-desc" type="text" bind:value={editing.description} />
        </div>
        <div class="stack">
          <label for="t-body">Body</label>
          <textarea
            id="t-body"
            style="font-family: 'JetBrains Mono', Consolas, monospace; min-height: 150px;"
            bind:value={editing.bodyTemplate}
          ></textarea>
          <span class="muted">
            Scriban-like <code>{'{{ placeholder }}'}</code> syntax. Globals available:
            <code>common_text</code>, <code>group_tags</code>, <code>theme_tags</code>, <code>group_name</code>.
          </span>
        </div>
        <div class="stack">
          <label for="t-dtags">Default theme tags</label>
          <input id="t-dtags" type="text" bind:value={defaultTagsInput} />
        </div>

        <div class="card-header" style="margin: 0.5rem 0 0;">
          <h4 style="margin: 0;">Placeholders</h4>
          <button class="btn btn-outline btn-sm" onclick={addPlaceholder}>+ Add placeholder</button>
        </div>

        {#if editing.placeholderSchema.length === 0}
          <p class="muted">No placeholders yet.</p>
        {:else}
          <div class="stack-lg">
            {#each editing.placeholderSchema as p, idx (idx)}
              <div
                style="border: 1px solid var(--vk-border); border-radius: 6px; padding: 0.6rem;"
              >
                <div class="row" style="align-items: flex-end;">
                  <label class="stack grow">
                    <span>Key</span>
                    <input type="text" bind:value={p.key} />
                  </label>
                  <label class="stack grow">
                    <span>Display name</span>
                    <input type="text" bind:value={p.displayName} />
                  </label>
                  <label class="stack">
                    <span>Type</span>
                    <select bind:value={p.type}>
                      {#each typeOptions as opt (opt.v)}
                        <option value={opt.v}>{opt.label}</option>
                      {/each}
                    </select>
                  </label>
                  <label class="row" style="gap: 0.3rem; align-self: center;">
                    <input type="checkbox" bind:checked={p.isRequired} /> Required
                  </label>
                  <button class="btn btn-danger btn-sm" onclick={() => removePlaceholder(p)}>🗑</button>
                </div>
              </div>
            {/each}
          </div>
        {/if}

        {#if editing.id != null}
          <div style="margin-top: 0.5rem;">
            <button class="btn btn-danger btn-sm" onclick={() => remove(editing!)}>
              🗑 Delete template
            </button>
          </div>
        {/if}
      </div>
    {/if}
  </section>
</div>
