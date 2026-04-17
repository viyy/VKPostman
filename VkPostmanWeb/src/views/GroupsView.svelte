<script lang="ts">
  import { liveQuery } from 'dexie';
  import { db, createGroup, saveGroup, deleteGroup } from '../lib/db';
  import type { TargetGroup } from '../lib/types';

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

  function edit(g: TargetGroup) {
    editing = { ...g };
    tagsInput = (g.mandatoryTags ?? []).join(' ');
  }

  function cancel() {
    editing = null;
  }

  async function addNew() {
    // Same UX as Templates: create a blank row, open it in the side editor. The
    // user fills Display name / Screen name / template inline instead of through
    // a browser prompt() dialog, which looks jarring and can't be styled.
    const id = await createGroup('');
    const g = await db.groups.get(id);
    if (g) edit(g);
  }

  async function save() {
    if (!editing) return;
    editing.mandatoryTags = tagsInput
      .split(/\s+/)
      .map((t) => t.replace(/^#+/, '').trim())
      .filter(Boolean);
    // $state.snapshot unwraps Svelte's proxy so Dexie's structuredClone-based
    // put() sees a plain object — safer than trusting the proxy to round-trip.
    await saveGroup($state.snapshot(editing) as typeof editing);
    editing = null;
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
          <button class="btn btn-primary btn-sm" onclick={save}>💾 Save</button>
          <button class="btn btn-ghost btn-sm" onclick={cancel}>Cancel</button>
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
