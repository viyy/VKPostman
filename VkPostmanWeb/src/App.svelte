<script lang="ts">
  import DraftsView from './views/DraftsView.svelte';
  import TemplatesView from './views/TemplatesView.svelte';
  import GroupsView from './views/GroupsView.svelte';
  import PlaceholdersView from './views/PlaceholdersView.svelte';
  import StatsView from './views/StatsView.svelte';
  import { downloadExport, exportAll, importFromFile } from './lib/exchange';
  import { nav, type Tab } from './lib/nav.svelte';
  import { undo } from './lib/undo.svelte';
  import { db, createDraft } from './lib/db';
  import { gdrive } from './lib/gdrive.svelte';
  import { formatBytes, getStorageInfo, requestPersistentStorage, type StorageInfo } from './lib/storage';
  import DataModal from './views/DataModal.svelte';
  import GlobalSearch from './views/GlobalSearch.svelte';
  import { liveQuery } from 'dexie';
  import { localDateStr } from './lib/dates';
  import { i18n, t } from './lib/i18n.svelte';
  import type { PostDraft } from './lib/types';
  import {
    Send, Search, Moon, Sun, Database, Download, Upload, HardDrive, Lock,
    TriangleAlert, FileText, LayoutTemplate, Tags, Users, ChartColumn, X, CalendarClock, Languages,
  } from '@lucide/svelte';

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

  // "Backed up" = a local JSON export OR a Google Drive backup, whichever's newer.
  const lastBackupTime = $derived(Math.max(lastExportAt, gdrive.lastBackupAt));
  const daysSinceExport = $derived(
    lastBackupTime ? Math.floor((Date.now() - lastBackupTime) / 86_400_000) : Infinity,
  );
  const showBackupReminder = $derived(
    hasData && Date.now() > snoozeUntil && daysSinceExport >= BACKUP_INTERVAL_DAYS,
  );

  // ---- "Planned posts due" reminder (while the app is open) ----------------
  let allDrafts = $state<PostDraft[]>([]);
  let dueDismissed = $state(false);
  $effect(() => {
    const s = liveQuery(() => db.drafts.toArray()).subscribe({ next: (v) => (allDrafts = v) });
    return () => s.unsubscribe();
  });
  const dueCount = $derived.by(() => {
    const today = localDateStr();
    return allDrafts.filter((d) => {
      if (!d.plannedFor || d.plannedFor > today) return false;
      const t = d.targetGroupIds ?? [];
      const p = d.postedGroupIds ?? [];
      const fully = t.length > 0 && t.every((id) => p.includes(id));
      return !fully;
    }).length;
  });
  const showDueReminder = $derived(dueCount > 0 && !dueDismissed);

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

  // ---- Modals + keyboard shortcuts -----------------------------------------
  let showData = $state(false);
  let showSearch = $state(false);
  let showShortcuts = $state(false);

  async function newDraftShortcut() {
    const id = await createDraft();
    nav.openDraft(id);
  }

  function onGlobalKeydown(e: KeyboardEvent) {
    const mod = e.ctrlKey || e.metaKey;

    // Ctrl/Cmd+K → global search (works even while typing).
    if (mod && (e.key === 'k' || e.key === 'K')) {
      e.preventDefault();
      showSearch = true;
      return;
    }
    if (e.key === 'Escape') {
      showSearch = false;
      showShortcuts = false;
      showData = false;
      return;
    }

    // Remaining shortcuts are inert while typing or with a modifier held.
    const el = document.activeElement as HTMLElement | null;
    const typing =
      !!el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT' || el.isContentEditable);
    if (typing || mod) return;

    if (e.altKey) {
      const tabs: Record<string, Tab> = { '1': 'drafts', '2': 'templates', '3': 'placeholders', '4': 'groups', '5': 'stats' };
      const t = tabs[e.key];
      if (t) { e.preventDefault(); nav.tab = t; }
      return;
    }
    if (e.key === 'n') { e.preventDefault(); void newDraftShortcut(); }
    else if (e.key === '?') { e.preventDefault(); showShortcuts = !showShortcuts; }
  }

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
    <span class="brand"><Send size={18} /> VK Postman</span>
    <span class="muted" style="color: rgba(255,255,255,0.75); font-size: 0.78rem;">
      v{__APP_VERSION__}
    </span>
    <div style="flex: 1;"></div>
    <button
      class="quota"
      onclick={() => i18n.toggle()}
      title="Русский / English"
      aria-label="Toggle language"
    ><Languages size={14} /> {i18n.locale === 'ru' ? 'RU' : 'EN'}</button>
    {#if storage}
      <button
        class="quota"
        onclick={onQuotaClick}
        title={`Local storage: ${formatBytes(storage.usage)} of ${formatBytes(storage.quota)} used` +
          `${storage.quota ? ` (${((storage.usage / storage.quota) * 100).toFixed(1)}%)` : ''}` +
          (storage.persisted ? ' · persistent' : ' · click to make persistent')}
      >
        {#if storage.persisted}<Lock size={14} />{:else}<HardDrive size={14} />{/if}
        {formatBytes(storage.usage)}
      </button>
    {/if}
    <button
      class="icon-btn"
      onclick={() => (showSearch = true)}
      aria-label={t('Search everything (Ctrl+K)')}
      title={t('Search everything (Ctrl+K)')}
    ><Search size={20} /></button>
    <button
      class="icon-btn"
      onclick={toggleTheme}
      title={theme === 'dark' ? t('Light theme') : t('Dark theme')}
    >{#if theme === 'dark'}<Sun size={20} />{:else}<Moon size={20} />{/if}</button>
    <button
      class="icon-btn"
      onclick={() => (showData = true)}
      title={t('Data tools: export a selection · merge import')}
    ><Database size={20} /></button>
    <button
      class="icon-btn"
      onclick={doExport}
      title={t('Export all data (JSON backup)')}
    ><Download size={20} /></button>
    <button
      class="icon-btn"
      onclick={doImportClick}
      title={t('Import data — replace all (JSON)')}
    ><Upload size={20} /></button>
  </div>

  {#if showBackupReminder}
    <div class="io-banner backup">
      <TriangleAlert size={16} />
      {i18n.locale === 'ru'
        ? (daysSinceExport === Infinity ? 'Вы ещё не делали резервную копию.' : `Резервной копии не было ${daysSinceExport} дн.`)
        : (daysSinceExport === Infinity ? 'You haven’t backed up yet.' : `You haven’t backed up in ${daysSinceExport} days.`)}
      {t('Your data lives only in this browser.')}
      <button class="link-inline" onclick={doExport}>{t('Export now')}</button>
      <button class="link-inline muted-link" onclick={snoozeBackup}>{t('Remind me later')}</button>
    </div>
  {/if}

  {#if showDueReminder}
    <div class="io-banner due">
      <CalendarClock size={16} />
      {i18n.locale === 'ru'
        ? `Запланировано постов на сегодня/просрочено: ${dueCount}.`
        : `${dueCount} planned post${dueCount === 1 ? '' : 's'} due today or overdue.`}
      <button class="link-inline" onclick={() => { nav.tab = 'stats'; dueDismissed = true; }}>{t('View agenda')}</button>
      <button class="link-inline muted-link" onclick={() => (dueDismissed = true)}>{t('Dismiss')}</button>
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
    ><FileText size={16} /> {t('Drafts')}</button>
    <button
      class="tab"
      class:active={nav.tab === 'templates'}
      role="tab"
      aria-selected={nav.tab === 'templates'}
      onclick={() => (nav.tab = 'templates')}
    ><LayoutTemplate size={16} /> {t('Templates')}</button>
    <button
      class="tab"
      class:active={nav.tab === 'placeholders'}
      role="tab"
      aria-selected={nav.tab === 'placeholders'}
      onclick={() => (nav.tab = 'placeholders')}
    ><Tags size={16} /> {t('Placeholders')}</button>
    <button
      class="tab"
      class:active={nav.tab === 'groups'}
      role="tab"
      aria-selected={nav.tab === 'groups'}
      onclick={() => (nav.tab = 'groups')}
    ><Users size={16} /> {t('Groups')}</button>
    <button
      class="tab"
      class:active={nav.tab === 'stats'}
      role="tab"
      aria-selected={nav.tab === 'stats'}
      onclick={() => (nav.tab = 'stats')}
    ><ChartColumn size={16} /> {t('Stats')}</button>
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
      <button class="undo-x" aria-label="Dismiss" onclick={() => undo.dismiss()}><X size={16} /></button>
    </div>
  {/if}

  {#if showData}
    <DataModal
      onclose={() => (showData = false)}
      onresult={(msg) => { ioMessage = msg; void refreshStorage(); }}
    />
  {/if}

  {#if showSearch}
    <GlobalSearch onclose={() => (showSearch = false)} />
  {/if}

  {#if showShortcuts}
    <div class="overlay" role="presentation" onclick={() => (showShortcuts = false)}>
      <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
      <div class="sc-modal" role="dialog" aria-modal="true" tabindex="-1" aria-label="Keyboard shortcuts" onclick={(e) => e.stopPropagation()}>
        <div class="card-header">
          <h3 style="margin: 0;">{t('Keyboard shortcuts')}</h3>
          <button class="btn btn-ghost btn-sm" onclick={() => (showShortcuts = false)}>{t('Close')}</button>
        </div>
        <dl class="sc-list">
          <dt><kbd>Ctrl</kbd>+<kbd>K</kbd></dt><dd>{t('Open global search')}</dd>
          <dt><kbd>N</kbd></dt><dd>{t('New draft')}</dd>
          <dt><kbd>Alt</kbd>+<kbd>1</kbd>…<kbd>5</kbd></dt><dd>{t('Switch tabs (Drafts / Templates / Placeholders / Groups / Stats)')}</dd>
          <dt><kbd>Ctrl</kbd>+<kbd>Enter</kbd></dt><dd>{t('On Drafts: copy next unposted & open vk.com')}</dd>
          <dt><kbd>?</kbd></dt><dd>{t('Toggle this help')}</dd>
          <dt><kbd>Esc</kbd></dt><dd>{t('Close dialogs')}</dd>
        </dl>
        <p class="muted" style="margin: 0.5rem 0 0;">{t('Letter shortcuts are ignored while typing in a field.')}</p>
      </div>
    </div>
  {/if}
</div>

<svelte:window onkeydown={onGlobalKeydown} />

<style>
  .icon-btn {
    appearance: none;
    border: none;
    background: transparent;
    color: rgba(255, 255, 255, 0.9);
    display: inline-flex;
    align-items: center;
    justify-content: center;
    padding: 0.4rem 0.5rem;
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
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
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

  .overlay {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.45);
    display: flex;
    align-items: flex-start;
    justify-content: center;
    padding: 4rem 1rem;
    z-index: 90;
  }
  .sc-modal {
    background: var(--vk-surface);
    border: 1px solid var(--vk-border);
    border-radius: var(--radius);
    box-shadow: var(--shadow-md);
    padding: 1rem;
    width: 100%;
    max-width: 460px;
  }
  .sc-list {
    display: grid;
    grid-template-columns: auto 1fr;
    gap: 0.4rem 1rem;
    margin: 0;
    align-items: baseline;
  }
  .sc-list dt { white-space: nowrap; }
  .sc-list dd { margin: 0; color: var(--vk-text-secondary); }
  kbd {
    font-family: 'JetBrains Mono', Consolas, monospace;
    font-size: 0.8rem;
    background: var(--vk-surface-alt);
    border: 1px solid var(--vk-border-strong);
    border-radius: 4px;
    padding: 1px 5px;
  }

  .io-banner {
    padding: 0.55rem 1rem;
    font-size: 0.9rem;
  }
  .io-banner.ok  { background: var(--vk-banner-ok-bg); color: var(--vk-banner-ok-fg); }
  .io-banner.err { background: var(--vk-banner-err-bg); color: var(--vk-banner-err-fg); }
  .io-banner.due {
    background: var(--vk-accent);
    color: var(--vk-blue);
    display: flex;
    align-items: center;
    gap: 0.5rem;
    flex-wrap: wrap;
  }

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
