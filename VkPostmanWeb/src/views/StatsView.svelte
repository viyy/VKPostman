<script lang="ts">
  import { liveQuery } from 'dexie';
  import { db } from '../lib/db';
  import type {
    PlaceholderDefinition,
    PostDraft,
    PostTemplate,
    TargetGroup,
  } from '../lib/types';

  const draftsQuery       = liveQuery(() => db.drafts.toArray());
  const groupsQuery       = liveQuery(() => db.groups.toArray());
  const templatesQuery    = liveQuery(() => db.templates.toArray());
  const placeholdersQuery = liveQuery(() => db.placeholders.toArray());

  let drafts       = $state<PostDraft[]>([]);
  let groups       = $state<TargetGroup[]>([]);
  let templates    = $state<PostTemplate[]>([]);
  let placeholders = $state<PlaceholderDefinition[]>([]);

  $effect(() => { const s = draftsQuery.subscribe({ next: (v) => (drafts = v) }); return () => s.unsubscribe(); });
  $effect(() => { const s = groupsQuery.subscribe({ next: (v) => (groups = v) }); return () => s.unsubscribe(); });
  $effect(() => { const s = templatesQuery.subscribe({ next: (v) => (templates = v) }); return () => s.unsubscribe(); });
  $effect(() => { const s = placeholdersQuery.subscribe({ next: (v) => (placeholders = v) }); return () => s.unsubscribe(); });

  const groupsById = $derived(new Map(groups.map((g) => [g.id!, g])));

  /** A "post" = one group marked posted on one draft. */
  const totalPosts = $derived(drafts.reduce((n, d) => n + (d.postedGroupIds?.length ?? 0), 0));

  function isFullyPosted(d: PostDraft): boolean {
    const t = d.targetGroupIds ?? [];
    if (t.length === 0) return false;
    const p = d.postedGroupIds ?? [];
    return t.every((id) => p.includes(id));
  }

  const draftStatus = $derived.by(() => {
    let fully = 0, inProgress = 0, noGroups = 0;
    for (const d of drafts) {
      if ((d.targetGroupIds ?? []).length === 0) noGroups++;
      else if (isFullyPosted(d)) fully++;
      else inProgress++;
    }
    return { total: drafts.length, fully, inProgress, noGroups };
  });

  const activity = $derived.by(() => {
    const now = Date.now();
    const cut7 = now - 7 * 86_400_000;
    const cut30 = now - 30 * 86_400_000;
    let last7 = 0, last30 = 0, timestamped = 0;
    for (const d of drafts) {
      for (const iso of Object.values(d.postedAt ?? {})) {
        const t = new Date(iso).getTime();
        if (Number.isNaN(t)) continue;
        timestamped++;
        if (t >= cut7) last7++;
        if (t >= cut30) last30++;
      }
    }
    return { last7, last30, timestamped };
  });

  const postsPerGroup = $derived.by(() => {
    const counts = new Map<number, number>();
    for (const d of drafts)
      for (const id of d.postedGroupIds ?? []) counts.set(id, (counts.get(id) ?? 0) + 1);
    return groups
      .map((g) => ({ name: g.displayName, screen: g.screenName, count: counts.get(g.id!) ?? 0 }))
      .sort((a, b) => b.count - a.count || a.name.localeCompare(b.name));
  });

  const templateUsage = $derived.by(() => {
    const postsByTpl = new Map<number, number>();
    for (const d of drafts) {
      for (const gid of d.postedGroupIds ?? []) {
        const tplId = groupsById.get(gid)?.postTemplateId;
        if (tplId != null) postsByTpl.set(tplId, (postsByTpl.get(tplId) ?? 0) + 1);
      }
    }
    const groupsByTpl = new Map<number, number>();
    for (const g of groups)
      if (g.postTemplateId != null) groupsByTpl.set(g.postTemplateId, (groupsByTpl.get(g.postTemplateId) ?? 0) + 1);
    return templates
      .map((t) => ({ name: t.name, groups: groupsByTpl.get(t.id!) ?? 0, posts: postsByTpl.get(t.id!) ?? 0 }))
      .sort((a, b) => b.posts - a.posts || b.groups - a.groups || a.name.localeCompare(b.name));
  });

  const maxGroupPosts = $derived(Math.max(1, ...postsPerGroup.map((r) => r.count)));
  const maxTplPosts   = $derived(Math.max(1, ...templateUsage.map((r) => r.posts)));

  const pct = (n: number, max: number) => `${Math.round((n / max) * 100)}%`;
</script>

<div class="stats">
  <!-- Overview tiles -->
  <div class="card">
    <h3 style="margin: 0 0 0.6rem;">Overview</h3>
    <div class="tiles">
      <div class="tile"><span class="num">{drafts.length}</span><span class="lbl">Drafts</span></div>
      <div class="tile"><span class="num">{templates.length}</span><span class="lbl">Templates</span></div>
      <div class="tile"><span class="num">{groups.length}</span><span class="lbl">Groups</span></div>
      <div class="tile"><span class="num">{placeholders.length}</span><span class="lbl">Placeholders</span></div>
      <div class="tile"><span class="num">{totalPosts}</span><span class="lbl">Posts marked</span></div>
    </div>
  </div>

  <div class="two-col">
    <!-- Draft status -->
    <div class="card">
      <h3 style="margin: 0 0 0.6rem;">Draft status</h3>
      <div class="kv"><span>✓ Fully posted</span><strong>{draftStatus.fully}</strong></div>
      <div class="kv"><span>In progress</span><strong>{draftStatus.inProgress}</strong></div>
      <div class="kv"><span>No groups yet</span><strong>{draftStatus.noGroups}</strong></div>
    </div>

    <!-- Activity -->
    <div class="card">
      <h3 style="margin: 0 0 0.6rem;">Posting activity</h3>
      <div class="kv"><span>Last 7 days</span><strong>{activity.last7}</strong></div>
      <div class="kv"><span>Last 30 days</span><strong>{activity.last30}</strong></div>
      <p class="muted" style="margin: 0.5rem 0 0;">
        Based on {activity.timestamped} timestamped post{activity.timestamped === 1 ? '' : 's'}.
        Posts marked before timestamps were added aren't dated.
      </p>
    </div>
  </div>

  <!-- Posts per group -->
  <div class="card">
    <h3 style="margin: 0 0 0.6rem;">Posts per group</h3>
    {#if postsPerGroup.length === 0}
      <p class="muted">No groups yet.</p>
    {:else}
      {#each postsPerGroup as r (r.screen)}
        <div class="bar-row">
          <div class="bar-label" title={r.name}>{r.name}</div>
          <div class="bar-track">
            <div class="bar-fill" style="width: {pct(r.count, maxGroupPosts)};"></div>
          </div>
          <div class="bar-num">{r.count}</div>
        </div>
      {/each}
    {/if}
  </div>

  <!-- Template usage -->
  <div class="card">
    <h3 style="margin: 0 0 0.6rem;">Template usage</h3>
    {#if templateUsage.length === 0}
      <p class="muted">No templates yet.</p>
    {:else}
      {#each templateUsage as r (r.name)}
        <div class="bar-row">
          <div class="bar-label" title={r.name}>{r.name}</div>
          <div class="bar-track">
            <div class="bar-fill" style="width: {pct(r.posts, maxTplPosts)};"></div>
          </div>
          <div class="bar-num">
            {r.posts} <span class="muted">· {r.groups} group{r.groups === 1 ? '' : 's'}</span>
          </div>
        </div>
      {/each}
    {/if}
  </div>
</div>

<style>
  .stats { display: flex; flex-direction: column; gap: 0.85rem; }

  .two-col {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 0.85rem;
  }
  @media (max-width: 720px) {
    .two-col { grid-template-columns: 1fr; }
  }
  /* The cards inside .two-col already carry their own bottom margin; remove it
     so the grid gap controls spacing. */
  .two-col > .card { margin-bottom: 0; }

  .tiles {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(110px, 1fr));
    gap: 0.6rem;
  }
  .tile {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 0.15rem;
    padding: 0.75rem 0.5rem;
    background: var(--vk-surface-alt);
    border-radius: var(--radius-sm);
  }
  .tile .num { font-size: 1.6rem; font-weight: 700; color: var(--vk-blue); line-height: 1; }
  .tile .lbl { font-size: 0.8rem; color: var(--vk-text-secondary); }

  .kv {
    display: flex;
    justify-content: space-between;
    padding: 0.3rem 0;
    border-bottom: 1px solid var(--vk-border);
  }
  .kv:last-of-type { border-bottom: none; }

  .bar-row {
    display: grid;
    grid-template-columns: minmax(90px, 160px) 1fr auto;
    align-items: center;
    gap: 0.6rem;
    padding: 0.25rem 0;
  }
  .bar-label {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-size: 0.88rem;
  }
  .bar-track {
    background: var(--vk-surface-alt);
    border-radius: 999px;
    height: 0.7rem;
    overflow: hidden;
  }
  .bar-fill {
    height: 100%;
    background: var(--vk-blue);
    border-radius: 999px;
    transition: width 200ms ease;
    min-width: 2px;
  }
  .bar-num { font-size: 0.85rem; font-variant-numeric: tabular-nums; }
</style>
