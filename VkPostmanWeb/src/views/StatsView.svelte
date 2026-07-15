<script lang="ts">
  import { liveQuery } from 'dexie';
  import { db } from '../lib/db';
  import type {
    PlaceholderDefinition,
    PostDraft,
    PostTemplate,
    TargetGroup,
  } from '../lib/types';
  import { nav } from '../lib/nav.svelte';
  import { t } from '../lib/i18n.svelte';
  import { localDateStr, addDaysStr, formatPlanned } from '../lib/dates';
  import { Check, CalendarClock } from '@lucide/svelte';

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

  // Upcoming planned posts, bucketed relative to today. Fully-posted and
  // unplanned drafts are excluded.
  const agenda = $derived.by(() => {
    const today = localDateStr();
    const weekEnd = addDaysStr(7);
    const buckets: Record<'overdue' | 'today' | 'week' | 'later', PostDraft[]> = {
      overdue: [], today: [], week: [], later: [],
    };
    for (const d of drafts) {
      const p = d.plannedFor;
      if (!p || isFullyPosted(d)) continue;
      if (p < today) buckets.overdue.push(d);
      else if (p === today) buckets.today.push(d);
      else if (p <= weekEnd) buckets.week.push(d);
      else buckets.later.push(d);
    }
    for (const k of Object.keys(buckets) as (keyof typeof buckets)[]) {
      buckets[k].sort((a, b) => (a.plannedFor ?? '').localeCompare(b.plannedFor ?? ''));
    }
    return buckets;
  });
  const agendaTotal = $derived(
    agenda.overdue.length + agenda.today.length + agenda.week.length + agenda.later.length,
  );
  const agendaSections = $derived([
    { key: 'overdue', label: t('Overdue') },
    { key: 'today', label: t('Today') },
    { key: 'week', label: t('This week') },
    { key: 'later', label: t('Later') },
  ] as const);

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

  // ---- Calendar heatmap (last 52 weeks) ------------------------------------
  const postsByDay = $derived.by(() => {
    const m = new Map<string, number>();
    for (const d of drafts) {
      for (const iso of Object.values(d.postedAt ?? {})) {
        const dt = new Date(iso);
        if (Number.isNaN(dt.getTime())) continue;
        const key = localDateStr(dt);
        m.set(key, (m.get(key) ?? 0) + 1);
      }
    }
    return m;
  });

  interface HeatCell { date: string; count: number; inFuture: boolean; }

  const heatmap = $derived.by(() => {
    const today = new Date();
    const todayKey = localDateStr(today);
    const dow = today.getDay(); // 0=Sun … 6=Sat
    const start = new Date(today);
    start.setDate(start.getDate() - dow - 51 * 7);
    const weeks: HeatCell[][] = [];
    for (let w = 0; w < 52; w++) {
      const col: HeatCell[] = [];
      for (let d = 0; d < 7; d++) {
        const cell = new Date(start);
        cell.setDate(cell.getDate() + w * 7 + d);
        const key = localDateStr(cell);
        col.push({ date: key, count: postsByDay.get(key) ?? 0, inFuture: key > todayKey });
      }
      weeks.push(col);
    }
    return weeks;
  });

  function heatLevel(n: number): 0 | 1 | 2 | 3 | 4 {
    if (n <= 0) return 0;
    if (n === 1) return 1;
    if (n === 2) return 2;
    if (n <= 4) return 3;
    return 4;
  }
</script>

<div class="stats">
  <!-- Agenda -->
  <div class="card">
    <h3 style="margin: 0 0 0.6rem; display: inline-flex; align-items: center; gap: 0.4rem;">
      <CalendarClock size={17} /> {t('Agenda')}
    </h3>
    {#if agendaTotal === 0}
      <p class="muted" style="margin: 0;">
        {t('No planned posts. Set “Plan to post on” on a draft to see it here.')}
      </p>
    {:else}
      {#each agendaSections as sec (sec.key)}
        {#if agenda[sec.key].length > 0}
          <div class="agenda-section">
            <div class="agenda-head" class:overdue={sec.key === 'overdue'} class:today={sec.key === 'today'}>
              {sec.label} <span class="muted">({agenda[sec.key].length})</span>
            </div>
            {#each agenda[sec.key] as d (d.id)}
              <button class="agenda-item" onclick={() => nav.openDraft(d.id!)}>
                <span class="agenda-date">{formatPlanned(d.plannedFor ?? '')}</span>
                <span class="agenda-title">{d.title}</span>
              </button>
            {/each}
          </div>
        {/if}
      {/each}
    {/if}
  </div>

  <!-- Overview tiles -->
  <div class="card">
    <h3 style="margin: 0 0 0.6rem;">{t('Overview')}</h3>
    <div class="tiles">
      <div class="tile"><span class="num">{drafts.length}</span><span class="lbl">{t('Drafts')}</span></div>
      <div class="tile"><span class="num">{templates.length}</span><span class="lbl">{t('Templates')}</span></div>
      <div class="tile"><span class="num">{groups.length}</span><span class="lbl">{t('Groups')}</span></div>
      <div class="tile"><span class="num">{placeholders.length}</span><span class="lbl">{t('Placeholders')}</span></div>
      <div class="tile"><span class="num">{totalPosts}</span><span class="lbl">{t('Posts marked')}</span></div>
    </div>
  </div>

  <div class="two-col">
    <!-- Draft status -->
    <div class="card">
      <h3 style="margin: 0 0 0.6rem;">{t('Draft status')}</h3>
      <div class="kv"><span><Check size={14} class="inline-ico done-ico" /> {t('Fully posted')}</span><strong>{draftStatus.fully}</strong></div>
      <div class="kv"><span>{t('In progress')}</span><strong>{draftStatus.inProgress}</strong></div>
      <div class="kv"><span>{t('No groups yet')}</span><strong>{draftStatus.noGroups}</strong></div>
    </div>

    <!-- Activity -->
    <div class="card">
      <h3 style="margin: 0 0 0.6rem;">{t('Posting activity')}</h3>
      <div class="kv"><span>{t('Last 7 days')}</span><strong>{activity.last7}</strong></div>
      <div class="kv"><span>{t('Last 30 days')}</span><strong>{activity.last30}</strong></div>
      <p class="muted" style="margin: 0.5rem 0 0;">
        {t('Based on {n} timestamped posts. Posts marked before timestamps were added aren’t dated.', { n: activity.timestamped })}
      </p>
    </div>
  </div>

  <!-- Calendar heatmap (last 52 weeks) -->
  <div class="card">
    <h3 style="margin: 0 0 0.6rem;">{t('Posting calendar')}</h3>
    <div class="heatmap-wrap">
      <div class="heatmap">
        {#each heatmap as week, wi (wi)}
          <div class="hm-week">
            {#each week as day (day.date)}
              <div
                class="hm-cell l{heatLevel(day.count)}"
                class:future={day.inFuture}
                title={`${day.date} · ${day.count} ${day.count === 1 ? t('post') : t('posts')}`}
              ></div>
            {/each}
          </div>
        {/each}
      </div>
      <div class="hm-legend">
        <span class="muted">{t('Less')}</span>
        <span class="hm-cell l0"></span>
        <span class="hm-cell l1"></span>
        <span class="hm-cell l2"></span>
        <span class="hm-cell l3"></span>
        <span class="hm-cell l4"></span>
        <span class="muted">{t('More')}</span>
      </div>
    </div>
  </div>

  <!-- Posts per group -->
  <div class="card">
    <h3 style="margin: 0 0 0.6rem;">{t('Posts per group')}</h3>
    {#if postsPerGroup.length === 0}
      <p class="muted">{t('No groups yet')}.</p>
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
    <h3 style="margin: 0 0 0.6rem;">{t('Template usage')}</h3>
    {#if templateUsage.length === 0}
      <p class="muted">{t('No templates yet.')}</p>
    {:else}
      {#each templateUsage as r (r.name)}
        <div class="bar-row">
          <div class="bar-label" title={r.name}>{r.name}</div>
          <div class="bar-track">
            <div class="bar-fill" style="width: {pct(r.posts, maxTplPosts)};"></div>
          </div>
          <div class="bar-num">
            {r.posts} <span class="muted">· {t('{n} groups', { n: r.groups })}</span>
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

  /* Calendar heatmap (GitHub-style). */
  .heatmap-wrap { overflow-x: auto; padding-bottom: 4px; }
  .heatmap { display: flex; gap: 3px; padding-bottom: 2px; }
  .hm-week { display: flex; flex-direction: column; gap: 3px; }
  .hm-cell {
    width: 11px;
    height: 11px;
    border-radius: 2px;
    background: var(--vk-surface-alt);
  }
  .hm-cell.future { opacity: 0.35; }
  .hm-cell.l1 { background: color-mix(in srgb, var(--vk-blue) 22%, var(--vk-surface-alt)); }
  .hm-cell.l2 { background: color-mix(in srgb, var(--vk-blue) 45%, var(--vk-surface-alt)); }
  .hm-cell.l3 { background: color-mix(in srgb, var(--vk-blue) 70%, var(--vk-surface-alt)); }
  .hm-cell.l4 { background: var(--vk-blue); }
  .hm-legend {
    display: flex;
    align-items: center;
    gap: 4px;
    margin-top: 0.5rem;
    font-size: 0.75rem;
  }
  .hm-legend .hm-cell { width: 11px; height: 11px; }

  .agenda-section { margin-bottom: 0.5rem; }
  .agenda-head {
    font-size: 0.8rem;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.3px;
    color: var(--vk-text-secondary);
    margin: 0.3rem 0 0.15rem;
  }
  .agenda-head.overdue { color: var(--vk-danger); }
  .agenda-head.today { color: var(--vk-blue); }
  .agenda-item {
    display: flex;
    align-items: baseline;
    gap: 0.6rem;
    width: 100%;
    text-align: left;
    appearance: none;
    border: none;
    background: transparent;
    font: inherit;
    color: inherit;
    padding: 0.3rem 0.4rem;
    border-radius: var(--radius-sm);
    cursor: pointer;
  }
  .agenda-item:hover { background: var(--vk-hover); }
  .agenda-date {
    flex-shrink: 0;
    width: 8.5rem;
    font-size: 0.82rem;
    color: var(--vk-text-secondary);
    font-variant-numeric: tabular-nums;
    white-space: nowrap;
  }
  .agenda-title { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
</style>
