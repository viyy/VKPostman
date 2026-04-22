<script lang="ts">
  import DraftsView from './views/DraftsView.svelte';
  import TemplatesView from './views/TemplatesView.svelte';
  import GroupsView from './views/GroupsView.svelte';
  import PlaceholdersView from './views/PlaceholdersView.svelte';
  import { downloadExport, exportAll, importFromFile } from './lib/exchange';

  type Tab = 'drafts' | 'templates' | 'placeholders' | 'groups';
  let tab = $state<Tab>((localStorage.getItem('vkp.tab') as Tab | null) ?? 'drafts');
  $effect(() => {
    localStorage.setItem('vkp.tab', tab);
  });

  // Settings sheet state
  let showMenu = $state(false);
  let importInput: HTMLInputElement | undefined = $state();
  let ioMessage = $state<{ ok: boolean; text: string } | null>(null);

  async function doExport() {
    showMenu = false;
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
    showMenu = false;
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
      offline · local-only
    </span>
    <div style="flex: 1;"></div>
    <div class="menu-wrap">
      <button
        class="icon-btn"
        onclick={() => (showMenu = !showMenu)}
        aria-label="Settings menu"
        aria-expanded={showMenu}
      >⋯</button>
      {#if showMenu}
        <div class="menu" role="menu">
          <button class="menu-item" role="menuitem" onclick={doExport}>
            ⬇️ Export data (JSON)
          </button>
          <button class="menu-item" role="menuitem" onclick={doImportClick}>
            ⬆️ Import data (JSON)
          </button>
        </div>
      {/if}
    </div>
  </div>

  {#if ioMessage}
    <div class="io-banner {ioMessage.ok ? 'ok' : 'err'}">{ioMessage.text}</div>
  {/if}

  <nav class="tabs" role="tablist">
    <button
      class="tab"
      class:active={tab === 'drafts'}
      role="tab"
      aria-selected={tab === 'drafts'}
      onclick={() => (tab = 'drafts')}
    >📝 Drafts</button>
    <button
      class="tab"
      class:active={tab === 'templates'}
      role="tab"
      aria-selected={tab === 'templates'}
      onclick={() => (tab = 'templates')}
    >📄 Templates</button>
    <button
      class="tab"
      class:active={tab === 'placeholders'}
      role="tab"
      aria-selected={tab === 'placeholders'}
      onclick={() => (tab = 'placeholders')}
    >🏷 Placeholders</button>
    <button
      class="tab"
      class:active={tab === 'groups'}
      role="tab"
      aria-selected={tab === 'groups'}
      onclick={() => (tab = 'groups')}
    >👥 Groups</button>
  </nav>

  <main class="content">
    {#if tab === 'drafts'}
      <DraftsView />
    {:else if tab === 'templates'}
      <TemplatesView />
    {:else if tab === 'placeholders'}
      <PlaceholdersView />
    {:else}
      <GroupsView />
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
</div>

<svelte:window onclick={(e) => {
  if (!showMenu) return;
  const t = e.target as HTMLElement;
  if (!t.closest?.('.menu-wrap')) showMenu = false;
}} />

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

  .menu-wrap {
    position: relative;
  }
  .menu {
    position: absolute;
    right: 0;
    top: calc(100% + 6px);
    min-width: 220px;
    background: #fff;
    color: #2c2d30;
    border: 1px solid #e7e8ec;
    border-radius: 8px;
    box-shadow: 0 10px 25px rgba(0, 0, 0, 0.14), 0 4px 10px rgba(0, 0, 0, 0.06);
    padding: 4px;
    z-index: 50;
  }
  .menu-item {
    display: block;
    width: 100%;
    text-align: left;
    padding: 0.55rem 0.75rem;
    appearance: none;
    background: transparent;
    border: none;
    font-family: inherit;
    font-size: 0.9rem;
    color: inherit;
    cursor: pointer;
    border-radius: 4px;
  }
  .menu-item:hover { background: #f4f5f7; }

  .io-banner {
    padding: 0.55rem 1rem;
    font-size: 0.9rem;
  }
  .io-banner.ok  { background: #E6F4E6; color: #1e5a1e; }
  .io-banner.err { background: #FCE4E4; color: #8a2424; }
</style>
