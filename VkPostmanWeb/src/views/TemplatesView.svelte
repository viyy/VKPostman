<script lang="ts">
  import { liveQuery } from 'dexie';
  import { db, createTemplate, saveTemplate, deleteTemplate, ensurePlaceholder } from '../lib/db';
  import {
    type PlaceholderDefinition,
    type PostTemplate,
  } from '../lib/types';
  import { createAutosave, type AutosaveStatus } from '../lib/autosave';
  import {
    BUILT_IN_PLACEHOLDERS,
    extractLibraryPlaceholderKeys,
    isBuiltInPlaceholder,
  } from '../lib/render';
  import { tick } from 'svelte';

  const templatesQuery = liveQuery(() => db.templates.orderBy('updatedAt').reverse().toArray());
  const libraryQuery   = liveQuery(() => db.placeholders.orderBy('key').toArray());

  let templates = $state<PostTemplate[] | undefined>(undefined);
  let library = $state<PlaceholderDefinition[]>([]);

  $effect(() => {
    const s = templatesQuery.subscribe({ next: (v) => (templates = v) });
    return () => s.unsubscribe();
  });
  $effect(() => {
    const s = libraryQuery.subscribe({ next: (v) => (library = v) });
    return () => s.unsubscribe();
  });

  let editing = $state<PostTemplate | null>(null);
  let defaultTagsInput = $state('');
  let saveStatus = $state<AutosaveStatus>('idle');

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
    if (!confirm(`Delete template "${t.name}"?`)) return;
    await deleteTemplate(t.id);
    if (editing?.id === t.id) editing = null;
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
   * fire-and-forget an ensure() to create it. Dexie's liveQuery refreshes
   * `library` automatically on the next microtask.
   */
  let _lastEnsuredKeys = new Set<string>();
  $effect(() => {
    if (!editing) return;
    const keys = extractLibraryPlaceholderKeys(editing.bodyTemplate);
    const existing = new Set(library.map((d) => d.key));
    for (const key of keys) {
      if (existing.has(key)) continue;
      if (_lastEnsuredKeys.has(key)) continue;
      _lastEnsuredKeys.add(key);
      void ensurePlaceholder(key);
    }
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
      case 'saving': return 'Saving…';
      case 'saved':  return '✓ Saved';
      case 'error':  return '⚠ Save failed';
      default:       return '';
    }
  });

  function typeLabel(t: number): string {
    switch (t) {
      case 1:  return 'VK link';
      case 4:  return 'wiki link';
      case 2:  return 'URL';
      case 3:  return 'tags';
      default: return 'text';
    }
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
            <span class="meta">{new Date(t.updatedAt).toLocaleString()}</span>
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
          <span class="muted" style="min-width: 5rem; text-align: right;">{statusLabel}</span>
          <button class="btn btn-ghost btn-sm" onclick={close}>Close</button>
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
              <span class="muted" style="font-size: 0.75rem;">Insert:</span>
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
                No matches. Type the name — a placeholder row will be added for you.
              </span>
            {/if}
          </div>

          <span class="muted">
            Scriban-like <code>{'{{ placeholder }}'}</code> syntax.
            New keys auto-appear on the Placeholders tab.
          </span>
        </div>
        <div class="stack">
          <label for="t-dtags">Default theme tags</label>
          <input id="t-dtags" type="text" bind:value={defaultTagsInput} />
        </div>

        <div>
          <h4 style="margin: 1rem 0 0.4rem;">Placeholders used</h4>
          <p class="muted" style="margin: 0 0 0.4rem;">
            Derived from the Body. Edit definitions on the <strong>Placeholders</strong> tab —
            changes propagate to every template using that key.
          </p>
          {#if usedPlaceholders.length === 0}
            <p class="muted">
              None yet. Type <code>{'{{ my_key }}'}</code> in the Body to add one.
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
