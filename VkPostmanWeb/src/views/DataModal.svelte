<script lang="ts">
  import { liveQuery } from 'dexie';
  import { db } from '../lib/db';
  import {
    downloadExport,
    exportAll,
    exportSubset,
    importFromJson,
    mergeFromJson,
    mergeFromFile,
    type SubsetSelection,
  } from '../lib/exchange';
  import { gdrive } from '../lib/gdrive.svelte';
  import type { PostDraft, PostTemplate, TargetGroup } from '../lib/types';
  import { Upload, Download, Cloud, CloudUpload, CloudDownload, LogOut } from '@lucide/svelte';

  interface Props {
    onclose: () => void;
    onresult: (msg: { ok: boolean; text: string }) => void;
  }
  let { onclose, onresult }: Props = $props();

  // Live lists for the export picker.
  const templatesQuery = liveQuery(() => db.templates.orderBy('name').toArray());
  const groupsQuery = liveQuery(() => db.groups.orderBy('displayName').toArray());
  const draftsQuery = liveQuery(() => db.drafts.orderBy('updatedAt').reverse().toArray());

  let templates = $state<PostTemplate[]>([]);
  let groups = $state<TargetGroup[]>([]);
  let drafts = $state<PostDraft[]>([]);

  $effect(() => { const s = templatesQuery.subscribe({ next: (v) => (templates = v) }); return () => s.unsubscribe(); });
  $effect(() => { const s = groupsQuery.subscribe({ next: (v) => (groups = v) }); return () => s.unsubscribe(); });
  $effect(() => { const s = draftsQuery.subscribe({ next: (v) => (drafts = v) }); return () => s.unsubscribe(); });

  // Selection sets.
  let selTpl = $state<Set<number>>(new Set());
  let selGrp = $state<Set<number>>(new Set());
  let selDrf = $state<Set<number>>(new Set());

  const selectedCount = $derived(selTpl.size + selGrp.size + selDrf.size);

  function toggle(set: Set<number>, id: number): Set<number> {
    const next = new Set(set);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    return next;
  }

  let importInput: HTMLInputElement | undefined = $state();

  // ---- Google Drive ---------------------------------------------------------
  let clientIdInput = $state(gdrive.clientId);
  let editingClientId = $state(!gdrive.configured);
  let restoreData = $state<unknown | null>(null);

  function fmtTime(ms: number): string {
    return ms ? new Date(ms).toLocaleString() : 'never';
  }

  function saveClientId() {
    gdrive.setClientId(clientIdInput);
    editingClientId = false;
  }

  async function driveConnect() {
    try {
      await gdrive.connect();
      onresult({ ok: true, text: 'Connected to Google Drive.' });
    } catch (err) {
      onresult({ ok: false, text: `Google sign-in failed: ${(err as Error).message}` });
    }
  }

  async function driveBackup() {
    try {
      const json = JSON.stringify(await exportAll());
      await gdrive.backup(json);
      onresult({ ok: true, text: 'Backed up to Google Drive.' });
    } catch (err) {
      onresult({ ok: false, text: `Drive backup failed: ${(err as Error).message}` });
    }
  }

  async function driveRestoreFetch() {
    try {
      const data = await gdrive.restore();
      if (data == null) {
        onresult({ ok: false, text: 'No backup found in your Drive yet.' });
        return;
      }
      restoreData = data; // ask Replace vs Merge below
    } catch (err) {
      onresult({ ok: false, text: `Drive restore failed: ${(err as Error).message}` });
    }
  }

  async function applyRestore(mode: 'replace' | 'merge') {
    if (restoreData == null) return;
    try {
      const sum = mode === 'replace' ? await importFromJson(restoreData) : await mergeFromJson(restoreData);
      restoreData = null;
      onresult({
        ok: true,
        text:
          `${mode === 'replace' ? 'Replaced from' : 'Merged from'} Drive: ` +
          `${sum.drafts} drafts, ${sum.groups} groups, ${sum.templates} templates, ${sum.placeholders} placeholders.`,
      });
      onclose();
    } catch (err) {
      onresult({ ok: false, text: `Restore failed: ${(err as Error).message}` });
    }
  }

  async function doExportSubset() {
    try {
      const sel: SubsetSelection = {
        templateIds: [...selTpl],
        groupIds: [...selGrp],
        draftIds: [...selDrf],
      };
      const data = await exportSubset(sel);
      const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
      downloadExport(data, `vk-postman-subset-${stamp}.json`);
      onresult({
        ok: true,
        text:
          `Exported ${data.drafts.length} drafts, ${data.groups.length} groups, ` +
          `${data.templates.length} templates, ${data.placeholders.length} placeholders ` +
          `(with dependencies).`,
      });
      onclose();
    } catch (err) {
      onresult({ ok: false, text: `Subset export failed: ${(err as Error).message}` });
    }
  }

  async function onMergePicked(e: Event) {
    const input = e.currentTarget as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    try {
      const sum = await mergeFromFile(file);
      onresult({
        ok: true,
        text:
          `Merged in ${sum.drafts} drafts, ${sum.groups} groups, ${sum.templates} templates, ` +
          `${sum.placeholders} new placeholders.`,
      });
      onclose();
    } catch (err) {
      onresult({ ok: false, text: `Merge import failed: ${(err as Error).message}` });
    }
  }
</script>

<div class="overlay" role="presentation" onclick={onclose}>
  <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
  <div class="modal" role="dialog" aria-modal="true" tabindex="-1" aria-label="Data tools" onclick={(e) => e.stopPropagation()}>
    <div class="card-header">
      <h3 style="margin: 0;">Data tools</h3>
      <button class="btn btn-ghost btn-sm" onclick={onclose}>Close</button>
    </div>

    <!-- ===== Google Drive ===== -->
    <section class="block">
      <h4 style="margin: 0 0 0.3rem; display: inline-flex; align-items: center; gap: 0.4rem;">
        <Cloud size={16} /> Google Drive backup
      </h4>

      {#if editingClientId || !gdrive.configured}
        <p class="muted" style="margin: 0 0 0.5rem;">
          Paste a Google OAuth <strong>Client ID</strong> (Web type). Create one in Google Cloud →
          Credentials, set the OAuth consent screen, and add this site's origin to
          “Authorized JavaScript origins”. The Client ID is public — safe to store here.
        </p>
        <div class="row" style="gap: 0.4rem;">
          <input
            type="text"
            class="grow"
            placeholder="xxxxx.apps.googleusercontent.com"
            bind:value={clientIdInput}
          />
          <button class="btn btn-primary btn-sm" disabled={!clientIdInput.trim()} onclick={saveClientId}>Save</button>
          {#if gdrive.configured}
            <button class="btn btn-ghost btn-sm" onclick={() => (editingClientId = false)}>Cancel</button>
          {/if}
        </div>
      {:else}
        <p class="muted" style="margin: 0 0 0.5rem;">
          Stores one hidden backup file in your Drive’s app folder. Last backup:
          <strong>{fmtTime(gdrive.lastBackupAt)}</strong>.
          <button type="button" class="link-inline" onclick={() => (editingClientId = true)}>change Client ID</button>
        </p>

        {#if !gdrive.connected}
          <button class="btn btn-outline btn-sm" disabled={gdrive.busy} onclick={driveConnect}>
            <Cloud size={15} /> Connect Google Drive
          </button>
        {:else}
          <div class="row" style="gap: 0.4rem; flex-wrap: wrap;">
            <button class="btn btn-primary btn-sm" disabled={gdrive.busy} onclick={driveBackup}>
              <CloudUpload size={15} /> Back up now
            </button>
            <button class="btn btn-outline btn-sm" disabled={gdrive.busy} onclick={driveRestoreFetch}>
              <CloudDownload size={15} /> Restore from Drive
            </button>
            <button class="btn btn-ghost btn-sm" onclick={() => gdrive.signOut()}>
              <LogOut size={15} /> Disconnect
            </button>
          </div>

          {#if restoreData != null}
            <div class="restore-prompt">
              <span>Backup downloaded. Apply it how?</span>
              <button class="btn btn-danger btn-sm" onclick={() => applyRestore('replace')}>Replace all</button>
              <button class="btn btn-outline btn-sm" onclick={() => applyRestore('merge')}>Merge / add</button>
              <button class="btn btn-ghost btn-sm" onclick={() => (restoreData = null)}>Cancel</button>
            </div>
          {/if}
        {/if}
      {/if}
    </section>

    <!-- ===== Merge import ===== -->
    <section class="block">
      <h4 style="margin: 0 0 0.3rem;">Import &amp; merge</h4>
      <p class="muted" style="margin: 0 0 0.5rem;">
        Add the records from a file <strong>alongside</strong> what you already have
        (nothing is overwritten). Use the top-bar import button instead to replace everything.
      </p>
      <button class="btn btn-outline btn-sm" onclick={() => importInput?.click()}>
        <Upload size={15} /> Choose file to merge…
      </button>
      <input
        bind:this={importInput}
        type="file"
        accept="application/json,.json"
        style="display: none;"
        onchange={onMergePicked}
      />
    </section>

    <!-- ===== Subset export ===== -->
    <section class="block">
      <h4 style="margin: 0 0 0.3rem;">Export a selection</h4>
      <p class="muted" style="margin: 0 0 0.5rem;">
        Pick records to export. Dependencies are included automatically
        (a draft brings its groups, a group brings its template, etc.).
      </p>

      <div class="pick-cols">
        <div class="pick-col">
          <div class="field-label">Drafts</div>
          {#if drafts.length === 0}
            <p class="muted">None.</p>
          {:else}
            {#each drafts as d (d.id)}
              <label class="pick"><input type="checkbox" checked={selDrf.has(d.id!)} onchange={() => (selDrf = toggle(selDrf, d.id!))} /> <span>{d.title}</span></label>
            {/each}
          {/if}
        </div>
        <div class="pick-col">
          <div class="field-label">Groups</div>
          {#if groups.length === 0}
            <p class="muted">None.</p>
          {:else}
            {#each groups as g (g.id)}
              <label class="pick"><input type="checkbox" checked={selGrp.has(g.id!)} onchange={() => (selGrp = toggle(selGrp, g.id!))} /> <span>{g.displayName}</span></label>
            {/each}
          {/if}
        </div>
        <div class="pick-col">
          <div class="field-label">Templates</div>
          {#if templates.length === 0}
            <p class="muted">None.</p>
          {:else}
            {#each templates as t (t.id)}
              <label class="pick"><input type="checkbox" checked={selTpl.has(t.id!)} onchange={() => (selTpl = toggle(selTpl, t.id!))} /> <span>{t.name}</span></label>
            {/each}
          {/if}
        </div>
      </div>

      <div class="row" style="margin-top: 0.6rem; justify-content: flex-end;">
        <button class="btn btn-primary btn-sm" disabled={selectedCount === 0} onclick={doExportSubset}>
          <Download size={15} /> Export {selectedCount} selected
        </button>
      </div>
    </section>
  </div>
</div>

<style>
  .overlay {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.45);
    display: flex;
    align-items: flex-start;
    justify-content: center;
    padding: 2.5rem 1rem;
    z-index: 80;
    overflow: auto;
  }
  .modal {
    background: var(--vk-surface);
    border: 1px solid var(--vk-border);
    border-radius: var(--radius);
    box-shadow: var(--shadow-md);
    padding: 1rem;
    width: 100%;
    max-width: 720px;
  }
  .block {
    padding: 0.75rem 0;
    border-top: 1px solid var(--vk-border);
  }
  .pick-cols {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 0.75rem;
  }
  @media (max-width: 640px) {
    .pick-cols { grid-template-columns: 1fr; }
  }
  .pick-col {
    border: 1px solid var(--vk-border);
    border-radius: var(--radius-sm);
    padding: 0.5rem;
    max-height: 240px;
    overflow: auto;
  }
  .pick {
    display: flex;
    align-items: center;
    gap: 0.45rem;
    padding: 0.25rem 0.15rem;
    cursor: pointer;
    font-size: 0.88rem;
  }
  .pick span {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .restore-prompt {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.4rem;
    margin-top: 0.6rem;
    padding: 0.6rem;
    border: 1px solid var(--vk-border);
    border-radius: var(--radius-sm);
    background: var(--vk-surface-alt);
    font-size: 0.9rem;
  }
</style>
