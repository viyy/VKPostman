import Dexie, { type EntityTable } from 'dexie';
import type { PostDraft, PostTemplate, TargetGroup } from './types';

/**
 * Local IndexedDB store. Schema v1 — if you change indexes or add stores,
 * bump the version number and add a `stores()` call for it; Dexie will run
 * the old-store → new-store migration automatically.
 */
class VkPostmanDb extends Dexie {
  templates!: EntityTable<PostTemplate, 'id'>;
  groups!: EntityTable<TargetGroup, 'id'>;
  drafts!: EntityTable<PostDraft, 'id'>;

  constructor() {
    super('vk-postman');
    this.version(1).stores({
      templates: '++id, name, updatedAt',
      groups: '++id, screenName, displayName, postTemplateId',
      drafts: '++id, title, updatedAt',
    });
  }
}

export const db = new VkPostmanDb();

// ---------------------------------------------------------------------------
// Tiny CRUD helpers — small enough that adding a service layer would be overkill.
// ---------------------------------------------------------------------------

export async function createDraft(): Promise<number> {
  const now = new Date();
  return db.drafts.add({
    title: `Draft — ${now.toLocaleString()}`,
    commonText: '',
    placeholderValues: {},
    themeTags: [],
    targetGroupIds: [],
    createdAt: now,
    updatedAt: now,
  });
}

export async function saveDraft(draft: PostDraft): Promise<void> {
  draft.updatedAt = new Date();
  await db.drafts.put(draft);
}

export async function deleteDraft(id: number): Promise<void> {
  await db.drafts.delete(id);
}

export async function createTemplate(): Promise<number> {
  const now = new Date();
  return db.templates.add({
    name: 'New template',
    description: '',
    bodyTemplate: '{{ common_text }}\n\n{{ group_tags }} {{ theme_tags }}',
    defaultThemeTags: [],
    placeholderSchema: [],
    createdAt: now,
    updatedAt: now,
  });
}

export async function saveTemplate(template: PostTemplate): Promise<void> {
  template.updatedAt = new Date();
  await db.templates.put(template);
}

export async function deleteTemplate(id: number): Promise<void> {
  await db.templates.delete(id);
}

export async function createGroup(screenName: string): Promise<number> {
  const cleaned = screenName.trim().replace(/^@/, '');
  return db.groups.add({
    screenName: cleaned,
    displayName: cleaned || 'New group',
    mandatoryTags: [],
    isActive: true,
    notes: '',
    createdAt: new Date(),
  });
}

export async function saveGroup(group: TargetGroup): Promise<void> {
  group.screenName = group.screenName.trim().replace(/^@/, '');
  await db.groups.put(group);
}

export async function deleteGroup(id: number): Promise<void> {
  await db.groups.delete(id);
}
