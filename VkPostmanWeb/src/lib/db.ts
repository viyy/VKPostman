import Dexie, { type EntityTable } from 'dexie';
import {
  PlaceholderType,
  type PlaceholderDefinition,
  type PostDraft,
  type PostTemplate,
  type TargetGroup,
} from './types';

/**
 * Local IndexedDB store. Schema v2 adds a top-level placeholders library
 * and strips the per-template `placeholderSchema` column. The upgrade
 * block walks existing templates' schemas and upserts them into the
 * new library (first-write-wins on key conflicts), then removes the
 * now-redundant field.
 */
class VkPostmanDb extends Dexie {
  templates!:    EntityTable<PostTemplate, 'id'>;
  groups!:       EntityTable<TargetGroup, 'id'>;
  drafts!:       EntityTable<PostDraft, 'id'>;
  placeholders!: EntityTable<PlaceholderDefinition, 'id'>;

  constructor() {
    super('vk-postman');

    // --- v1: initial schema (templates held placeholderSchema inline) ---
    this.version(1).stores({
      templates: '++id, name, updatedAt',
      groups:    '++id, screenName, displayName, postTemplateId',
      drafts:    '++id, title, updatedAt',
    });

    // --- v2: shared placeholder library ---
    this.version(2).stores({
      templates:    '++id, name, updatedAt',
      groups:       '++id, screenName, displayName, postTemplateId',
      drafts:       '++id, title, updatedAt',
      placeholders: '++id, &key',
    }).upgrade(async (tx) => {
      // Collect every inline placeholderSchema entry, dedup by key
      // (first-write-wins), upsert into the new library, and strip the
      // field from each template row.
      const seen = new Map<string, PlaceholderDefinition>();
      await tx.table('templates').toCollection().modify((t: any) => {
        const schema: Array<any> = Array.isArray(t.placeholderSchema) ? t.placeholderSchema : [];
        for (const def of schema) {
          if (!def?.key || seen.has(def.key)) continue;
          seen.set(def.key, {
            key: def.key,
            displayName: def.displayName ?? def.key,
            type: typeof def.type === 'number' ? def.type : PlaceholderType.Text,
            description: def.description ?? undefined,
            defaultValue: def.defaultValue ?? undefined,
            createdAt: new Date(),
            updatedAt: new Date(),
          });
        }
        delete t.placeholderSchema;
      });
      if (seen.size > 0) {
        await tx.table('placeholders').bulkAdd([...seen.values()]);
      }
    });
  }
}

export const db = new VkPostmanDb();

// ---------------------------------------------------------------------------
// CRUD helpers
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

// ---- Placeholder library ----

export async function savePlaceholder(def: PlaceholderDefinition): Promise<void> {
  def.updatedAt = new Date();
  await db.placeholders.put(def);
}

export async function deletePlaceholder(id: number): Promise<void> {
  await db.placeholders.delete(id);
}

/**
 * Ensures the library has an entry for `key`. Returns the existing or newly-created
 * row. Used by the template editor's auto-sync when the user types `{{ new_key }}`.
 */
export async function ensurePlaceholder(key: string): Promise<PlaceholderDefinition> {
  const existing = await db.placeholders.where('key').equals(key).first();
  if (existing) return existing;

  const displayName = toDisplayName(key);
  const now = new Date();
  const def: PlaceholderDefinition = {
    key,
    displayName,
    type: PlaceholderType.Text,
    createdAt: now,
    updatedAt: now,
  };
  const id = await db.placeholders.add(def);
  return { ...def, id };
}

function toDisplayName(key: string): string {
  const spaced = key.replace(/[_-]+/g, ' ').trim();
  if (spaced.length === 0) return key;
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}
