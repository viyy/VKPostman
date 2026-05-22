<script lang="ts">
  import DraftsView from './views/DraftsView.svelte';
  import TemplatesView from './views/TemplatesView.svelte';
  import GroupsView from './views/GroupsView.svelte';
  import PlaceholdersView from './views/PlaceholdersView.svelte';
  import StatsView from './views/StatsView.svelte';
  import { downloadExport, exportAll, importFromFile } from './lib/exchange';
  import { nav, type Tab } from './lib/nav.svelte';
  import { undo } from './lib/undo.svelte';

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

  async function doExport() {
    try {
      const data = await exportAll();
      downloadExport(data);
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
    <button
      class="icon-btn"
      onclick={toggleTheme}
      aria-label={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
      title={theme === 'dark' ? 'Light theme' : 'Dark theme'}
    >{theme === 'dark' ? '☀️' : '🌙'}</button>
    <button
      class="icon-btn"
      onclick={doExport}
      aria-label="Export data (JSON)"
      title="Export data (JSON)"
    >⬇️</button>
    <button
      class="icon-btn"
      onclick={doImportClick}
      aria-label="Import data (JSON)"
      title="Import data (JSON)"
    >⬆️</button>
  </div>

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
