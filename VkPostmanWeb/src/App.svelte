<script lang="ts">
  import DraftsView from './views/DraftsView.svelte';
  import TemplatesView from './views/TemplatesView.svelte';
  import GroupsView from './views/GroupsView.svelte';

  type Tab = 'drafts' | 'templates' | 'groups';
  // Persist the active tab across reloads so the app remembers where you were.
  let tab = $state<Tab>((localStorage.getItem('vkp.tab') as Tab | null) ?? 'drafts');
  $effect(() => {
    localStorage.setItem('vkp.tab', tab);
  });
</script>

<div class="app">
  <div class="topbar">
    <span class="brand">✉️ VK Postman</span>
    <span class="muted" style="color: rgba(255,255,255,0.75); font-size: 0.78rem;">
      offline · local-only
    </span>
  </div>

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
    {:else}
      <GroupsView />
    {/if}
  </main>
</div>
