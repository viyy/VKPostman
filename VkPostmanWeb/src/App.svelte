<script lang="ts">
  import DraftsView from './views/DraftsView.svelte';
  import TemplatesView from './views/TemplatesView.svelte';
  import GroupsView from './views/GroupsView.svelte';
  import PlaceholdersView from './views/PlaceholdersView.svelte';
  import StatsView from './views/StatsView.svelte';
  import { downloadExport, exportAll, importFromFile } from './lib/exchange';
  import { nav, type Tab } from './lib/nav.svelte';
  import { undo } from './lib/undo.svelte';
  import { db } from './lib/db';
  import { formatBytes, getStorageInfo, requestPersistentStorage, type StorageInfo } from './lib/storage';
  import DataModal from './views/DataModal.svelte';

  // Restore the last-used tab, then persist any change. The active tab itself
  // lives in the shared nav store so other views can switch tabs (e.g. a
  // template link on the Drafts page jumping to the Templates tab).
  const savedTab = localStorage.getItem('vkp.tab') as Tab | null;
  if (savedTab) nav.tab = savedTab;
  $effect(() => {
    localStorage.setItem('vkp.tab', nav.tab);
  });

  // ---- Theme (Catppuccin: Latte = light, Mocha = dark) --------------------
  // Default to the user's saved choice, else their OS preference.
  type Theme = 'light' | 'dark';
  let theme = $state<Theme>(
    (localStorage.getItem('vkp.theme') as Theme | null) ??
      (window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'),
  );
  $effect(() => {
    document.documentElement.dataset.theme = theme;
    localStorage.setItem('vkp.theme', theme);
    // Keep the mobile browser chrome in sync with the top bar colour.
    document
      .querySelector('meta[name="theme-color"]')
      ?.setAttribute('content', theme === 'dark' ? '#181825' : '#7287fd');
  });
  function toggleTheme() {
    theme = theme === 'dark' ? 'light' : 'dark';
  }

  // Import/export state
  let importInput: HTMLInputElement | undefined = $state();
  let ioMessage = $state<{ ok: boolean; text: string } | null>(null);

  // ---- Backup reminder -----------------------------------------------------
  const BACKUP_INTERVAL_DAYS = 7;
  const SNOOZE_DAYS = 3;
  let lastExportAt = $state<number>(Number(localStorage.getItem('vkp.lastExportAt')) || 0);
  let snoozeUntil = $state<number>(Number(localStorage.getItem('vkp.backupSnoozeUntil')) || 0);
  let hasData = $state(false);

  function markExported() {
    lastExportAt = Date.now();
    localStorage.setItem('vkp.lastExportAt', String(lastExportAt));
  }
  function snoozeBackup() {
    snoozeUntil = Date.now() + SNOOZE_DAYS * 86_400_000;
    localStorage.setItem('vkp.backupSnoozeUntil', String(snoozeUntil));
  }

  const daysSinceExport = $derived(
    lastExportAt ? Math.floor((Date.now() - lastExportAt) / 86_400_000) : Infinity,
  );
  const showBackupReminder = $derived(
    hasData && Date.now() > snoozeUntil && daysSinceExport >= BACKUP_INTERVAL_DAYS,
  );

  // ---- Storage quota indicator ---------------------------------------------
  let storage = $state<StorageInfo | null>(null);
  async function refreshStorage() {
    storage = await getStorageInfo();
  }
  async function onQuotaClick() {
    const ok = await requestPersistentStorage();
    await refreshStorage();
    ioMessage = ok
      ? { ok: true, text: 'Storage marked persistent — the browser won’t evict your data.' }
      : { ok: false, text: 'The browser declined persistent storage (data may still be evicted).' };
  }

  $effect(() => {
    void refreshStorage();
    void (async () => {
      const [t, g, d] = await Promise.all([db.templates.count(), db.groups.count(), db.drafts.count()]);
      hasData = t + g + d > 0;
    })();
    const id = setInterval(refreshStorage, 60_000);
    return () => clearInterval(id);
  });

  // ---- Data tools modal ----------------------------------------------------
  let showData = $state(false);

  async function doExport() {
    try {
      const data = await exportAll();
      downloadExport(data);
      markExported();
      void refreshStorage();
      ioMessage = {
        ok: true,
        text:
          `Downloaded ${data.placeholders.length} placeholders, ` +
          `${data.templates.length} templates, ` +
          `${data.groups.length} groups, ${data.drafts.length} drafts.`,
      };
    } catch (err) {
      ioMessage = { ok: false, text: `Export failed: ${(err as Error).message}` };
    }
  }

  function doImportClick() {
    importInput?.click();
  }

  async function onImportPicked(e: Event) {
    const input = e.currentTarget as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // allow re-picking the same file later
    if (!file) return;

    if (!confirm(
      `Import "${file.name}" will REPLACE all existing templates, groups, and drafts ` +
      `with the file's contents. Continue?`,
    )) return;

    try {
      const summary = await importFromFile(file);
      hasData = summary.templates + summary.groups + summary.drafts > 0;
      void refreshStorage();
      ioMessage = {
        ok: true,
        text:
          `Imported ${summary.placeholders} placeholders, ` +
          `${summary.templates} templates, ` +
          `${summary.groups} groups, ${summary.drafts} drafts.`,
      };
    } catch (err) {
      ioMessage = { ok: false, text: `Import failed: ${(err as Error).message}` };
    }
  }

  $effect(() => {
    if (!ioMessage) return;
    const t = setTimeout(() => (ioMessage = null), 5000);
    return () => clearTimeout(t);
  });
</script>

<div class="app">
  <div class="topbar">
    <span class="brand">✉️ VK Postman</span>
    <span class="muted" style="color: rgba(255,255,255,0.75); font-size: 0.78rem;">
      v{__APP_VERSION__} · offline · local-only
    </span>
    <div style="flex: 1;"></div>
    {#if storage}
      <button
        class="quota"
        onclick={onQuotaClick}
        title={`Local storage: ${formatBytes(storage.usage)} of ${formatBytes(storage.quota)} used` +
          `${storage.quota ? ` (${((storage.usage / storage.quota) * 100).toFixed(1)}%)` : ''}` +
          (storage.persisted ? ' · persistent' : ' · click to make persistent')}
      >
        {storage.persisted ? '🔒' : '💾'} {formatBytes(storage.usage)}
      </button>
    {/if}
    <button
      class="icon-btn"
      onclick={toggleTheme}
      aria-label={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
      title={theme === 'dark' ? 'Light theme' : 'Dark theme'}
    >{theme === 'dark' ? '☀️' : '🌙'}</button>
    <button
      class="icon-btn"
      onclick={() => (showData = true)}
      aria-label="Data tools (subset export / merge import)"
      title="Data tools: export a selection · merge import"
    >🗂️</button>
    <button
      class="icon-btn"
      onclick={doExport}
      aria-label="Export all data (JSON backup)"
      title="Export all data (JSON backup)"
    >⬇️</button>
    <button
      class="icon-btn"
      onclick={doImportClick}
      aria-label="Import data — replace all (JSON)"
      title="Import data — replace all (JSON)"
    >⬆️</button>
  </div>

  {#if showBackupReminder}
    <div class="io-banner backup">
      ⚠ You haven’t backed up
      {#if daysSinceExport === Infinity}yet{:else}in {daysSinceExport} days{/if}.
      Your data lives only in this browser.
      <button class="link-inline" onclick={doExport}>Export now</button>
      <button class="link-inline muted-link" onclick={snoozeBackup}>Remind me later</button>
    </div>
  {/if}

  {#if ioMessage}
    <div class="io-banner {ioMessage.ok ? 'ok' : 'err'}">{ioMessage.text}</div>
  {/if}

  <div class="tabs" role="tablist" aria-label="Sections">
    <button
      class="tab"
      class:active={nav.tab === 'drafts'}
      role="tab"
      aria-selected={nav.tab === 'drafts'}
      onclick={() => (nav.tab = 'drafts')}
    >📝 Drafts</button>
    <button
      class="tab"
      class:active={nav.tab === 'templates'}
      role="tab"
      aria-selected={nav.tab === 'templates'}
      onclick={() => (nav.tab = 'templates')}
    >📄 Templates</button>
    <button
      class="tab"
      class:active={nav.tab === 'placeholders'}
      role="tab"
      aria-selected={nav.tab === 'placeholders'}
      onclick={() => (nav.tab = 'placeholders')}
    >🏷 Placeholders</button>
    <button
      class="tab"
      class:active={nav.tab === 'groups'}
      role="tab"
      aria-selected={nav.tab === 'groups'}
      onclick={() => (nav.tab = 'groups')}
    >👥 Groups</button>
    <button
      class="tab"
      class:active={nav.tab === 'stats'}
      role="tab"
      aria-selected={nav.tab === 'stats'}
      onclick={() => (nav.tab = 'stats')}
    >📊 Stats</button>
  </div>

  <main class="content">
    {#if nav.tab === 'drafts'}
      <DraftsView />
    {:else if nav.tab === 'templates'}
      <TemplatesView />
    {:else if nav.tab === 'placeholders'}
      <PlaceholdersView />
    {:else if nav.tab === 'groups'}
      <GroupsView />
    {:else}
      <StatsView />
    {/if}
  </main>

  <!-- Hidden file input driven by the Import menu item. -->
  <input
    bind:this={importInput}
    type="file"
    accept="application/json,.json"
    style="display: none;"
    onchange={onImportPicked}
  />

  {#if undo.message}
    <div class="undo-toast" role="status">
      <span>{undo.message}</span>
      <button class="undo-btn" onclick={() => undo.undo()}>Undo</button>
      <button class="undo-x" aria-label="Dismiss" onclick={() => undo.dismiss()}>✕</button>
    </div>
  {/if}

  {#if showData}
    <DataModal
      onclose={() => (showData = false)}
      onresult={(msg) => { ioMessage = msg; void refreshStorage(); }}
    />
  {/if}
</div>

<style>
  .icon-btn {
    appearance: none;
    border: none;
    background: transparent;
    color: rgba(255, 255, 255, 0.9);
    padding: 0.3rem 0.55rem;
    font-size: 1.3rem;
    line-height: 1;
    border-radius: 6px;
    cursor: pointer;
  }
  .icon-btn:hover { background: rgba(255, 255, 255, 0.15); }

  .quota {
    appearance: none;
    border: none;
    background: transparent;
    color: rgba(255, 255, 255, 0.85);
    font: inherit;
    font-size: 0.78rem;
    padding: 0.25rem 0.5rem;
    border-radius: 6px;
    cursor: pointer;
    white-space: nowrap;
  }
  .quota:hover { background: rgba(255, 255, 255, 0.15); }

  .io-banner.backup {
    background: var(--vk-warning);
    color: #1a1300;
    display: flex;
    align-items: center;
    gap: 0.5rem;
    flex-wrap: wrap;
  }
  .link-inline {
    appearance: none;
    border: none;
    background: transparent;
    color: inherit;
    font: inherit;
    font-weight: 700;
    text-decoration: underline;
    cursor: pointer;
    padding: 0.1rem 0.3rem;
  }
  .link-inline.muted-link { font-weight: 500; opacity: 0.8; }

  .io-banner {
    padding: 0.55rem 1rem;
    font-size: 0.9rem;
  }
  .io-banner.ok  { background: var(--vk-banner-ok-bg); color: var(--vk-banner-ok-fg); }
  .io-banner.err { background: var(--vk-banner-err-bg); color: var(--vk-banner-err-fg); }

  .undo-toast {
    position: fixed;
    left: 50%;
    bottom: 1.25rem;
    transform: translateX(-50%);
    display: flex;
    align-items: center;
    gap: 0.75rem;
    background: var(--vk-text);
    color: var(--vk-bg);
    padding: 0.55rem 0.6rem 0.55rem 1rem;
    border-radius: 999px;
    box-shadow: var(--shadow-md);
    z-index: 100;
    font-size: 0.9rem;
    max-width: calc(100vw - 2rem);
  }
  .undo-btn {
    appearance: none;
    border: none;
    background: transparent;
    color: var(--vk-blue);
    font: inherit;
    font-weight: 700;
    cursor: pointer;
    padding: 0.2rem 0.5rem;
    border-radius: 6px;
  }
  .undo-btn:hover { background: rgba(127, 127, 127, 0.2); }
  .undo-x {
    appearance: none;
    border: none;
    background: transparent;
    color: inherit;
    opacity: 0.6;
    font: inherit;
    cursor: pointer;
    padding: 0.2rem 0.45rem;
    border-radius: 6px;
  }
  .undo-x:hover { opacity: 1; background: rgba(127, 127, 127, 0.2); }
</style>
