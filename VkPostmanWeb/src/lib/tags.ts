// A live, de-duplicated set of every tag the user has typed anywhere —
// group mandatory tags, template default theme tags, and draft theme tags.
// Powers the tag-autocomplete suggestion chips in the editors.

import { liveQuery, type Observable } from 'dexie';
import { db } from './db';

export function knownTagsQuery(): Observable<string[]> {
  return liveQuery(async () => {
    const [groups, templates, drafts] = await Promise.all([
      db.groups.toArray(),
      db.templates.toArray(),
      db.drafts.toArray(),
    ]);
    const set = new Set<string>();
    for (const g of groups) for (const t of g.mandatoryTags ?? []) set.add(t);
    for (const t of templates) for (const x of t.defaultThemeTags ?? []) set.add(x);
    for (const d of drafts) for (const x of d.themeTags ?? []) set.add(x);
    return [...set].sort((a, b) => a.localeCompare(b));
  });
}
