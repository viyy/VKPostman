<script lang="ts">
  import { liveQuery } from 'dexie';
  import { db, createGroup, saveGroup, deleteGroup } from '../lib/db';
  import type { TargetGroup } from '../lib/types';
  import { createAutosave, type AutosaveStatus } from '../lib/autosave';

  // Live IndexedDB queries — re-run automatically when the data changes.
  const groupsQuery = liveQuery(() => db.groups.orderBy('displayName').toArray());
  const templatesQuery = liveQuery(() => db.templates.orderBy('name').toArray());

  let groups = $state<TargetGroup[] | undefined>(undefined);
  let templates = $state<Array<{ id?: number; name: string }>>([]);

  $effect(() => {
    const sub = groupsQuery.subscribe({ next: (v) => (groups = v) });
    return () => sub.unsubscribe();
  });
  $effect(() => {
    const sub = templatesQuery.subscribe({ next: (v) => (templates = v) });
    return () => sub.unsubscribe();
  });

  let editing = $state<TargetGroup | null>(null);
  let tagsInput = $state('');
  let saveStatus = $state<AutosaveStatus>('idle');

  // Keep editing.mandatoryTags in sync with the input as the user types.
  $effect(() => {
    if (!editing) return;
    editing.mandatoryTags = tagsInput
      .split(/\s+/)
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

  async function removeGroup(g: TargetGroup) {
    if (!g.id) return;
    if (!confirm(`Delete group "${g.displayName}"?`)) return;
    await deleteGroup(g.id);
    if (editing?.id === g.id) editing = null;
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
      <button class="btn btn-primary btn-sm" onclick={addNew}>+ Add</button>
    </div>

    {#if !groups}
      <p class="muted">Loading…</p>
    {:else if groups.length === 0}
      <p class="muted">No groups yet. Click <em>+ Add</em> to create one.</p>
    {:else}
      <div class="list">
        {#each groups as g (g.id)}
          <button
            class="list-item"
            class:active={editing?.id === g.id}
            onclick={() => edit(g)}
          >
            <strong>{g.displayName}</strong>
            <span class="meta">
              @{g.screenName} · template: <em>{templateName(g.postTemplateId)}</em>
            </span>
          </button>
        {/each}
      </div>
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
          <!--
            Using value + onchange (not bind:value). `bind:value` on a select whose
            options come from a liveQuery has a race: on first render the options
            list is empty, the browser forces the selection to the first option
            ("— none —"), and bind:value writes that back, clobbering the real id.
          -->
          <select
            id="g-template"
            value={editing.postTemplateId ?? ''}
            onchange={(e) => {
              const raw = (e.currentTarget as HTMLSelectElement).value;
              editing!.postTemplateId = raw === '' ? undefined : Number(raw);
            }}
          >
            <option value="">— none —</option>
            {#each templates as t (t.id)}
              <option value={t.id}>{t.name}</option>
            {/each}
          </select>
          <span class="muted">A group without a template can't be a draft target.</span>
        </div>
        <div class="stack">
          <label for="g-tags">Mandatory tags (space-separated)</label>
          <input id="g-tags" type="text" bind:value={tagsInput} />
          <span class="muted">Appended to posts via <code>{'{{ group_tags }}'}</code>.</span>
        </div>
        <label class="row" style="gap: 0.5rem; font-weight: 500;">
          <input type="checkbox" bind:checked={editing.isActive} />
          <span>Active</span>
        </label>
        <div class="stack">
          <label for="g-notes">Notes</label>
          <textarea id="g-notes" bind:value={editing.notes}></textarea>
        </div>

        {#if editing.id != null}
          <div>
            <button class="btn btn-danger btn-sm" onclick={() => removeGroup(editing!)}>
              🗑 Delete group
            </button>
          </div>
        {/if}
      </div>
    {/if}
  </section>
</div>
