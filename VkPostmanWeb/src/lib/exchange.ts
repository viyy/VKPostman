// ---------------------------------------------------------------------------
// JSON export / import for interchange between the desktop (WPF) build and
// this PWA. File shape:
//
//   {
//     "formatVersion": 1,
//     "exportedAt": "2026-04-17T12:34:56.000Z",
//     "app": "vk-postman",
//     "templates": [ ... ],
//     "groups":    [ ... ],
//     "drafts":    [ ... ]
//   }
//
// Import strategy: REPLACE. We wipe the three tables and re-insert the
// payload, **remapping IDs**: old ids from the file get fresh ids assigned
// locally, and cross-references (group.postTemplateId, draft.targetGroupIds)
// are rewritten to the new ids. This means every import is self-consistent
// regardless of which ids the source database happened to use.
// ---------------------------------------------------------------------------

import { db } from './db';
import type {
  PlaceholderDefinition,
  PostDraft,
  PostTemplate,
  TargetGroup,
} from './types';

export const FORMAT_VERSION = 1;

export interface ExportFile {
  formatVersion: number;
  exportedAt: string;
  app: 'vk-postman';
  templates: PostTemplate[];
  groups: TargetGroup[];
  drafts: PostDraft[];
}

export async function exportAll(): Promise<ExportFile> {
  const [templates, groups, drafts] = await Promise.all([
    db.templates.toArray(),
    db.groups.toArray(),
    db.drafts.toArray(),
  ]);
  return {
    formatVersion: FORMAT_VERSION,
    exportedAt: new Date().toISOString(),
    app: 'vk-postman',
    templates,
    groups,
    drafts,
  };
}

export function toJsonBlob(data: ExportFile): Blob {
  // Pretty-printed with 2-space indent so humans can diff / hand-edit.
  return new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
}

/** Kicks off a browser download of the export file. */
export function downloadExport(data: ExportFile, filename?: string): void {
  const blob = toJsonBlob(data);
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
  a.download = filename ?? `vk-postman-${stamp}.json`;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  // Give the browser a tick to consume the URL before we revoke it.
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export interface ImportSummary {
  templates: number;
  groups: number;
  drafts: number;
}

/**
 * Read a JSON file (from <input type="file">), validate the shape,
 * REPLACE everything in the DB with its contents, remapping IDs.
 */
export async function importFromFile(file: File): Promise<ImportSummary> {
  const text = await file.text();
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch (err) {
    throw new Error(`That file isn't valid JSON: ${(err as Error).message}`);
  }
  return importFromJson(parsed);
}

/** Exposed so the WPF/PWA tests can pass an already-parsed object. */
export async function importFromJson(parsed: unknown): Promise<ImportSummary> {
  const payload = assertExportFile(parsed);

  // Run as one Dexie transaction: partial imports are worse than none.
  return db.transaction('rw', [db.templates, db.groups, db.drafts], async () => {
    await Promise.all([
      db.templates.clear(),
      db.groups.clear(),
      db.drafts.clear(),
    ]);

    // --- templates ---
    const templateIdMap = new Map<number, number>();
    for (const t of payload.templates) {
      const oldId = t.id;
      const { id: _omit, ...rest } = t as PostTemplate & { id?: number };
      const newId = await db.templates.add({
        ...rest,
        createdAt: coerceDate(rest.createdAt),
        updatedAt: coerceDate(rest.updatedAt),
      } as PostTemplate);
      if (oldId != null) templateIdMap.set(oldId, newId);
    }

    // --- groups ---
    const groupIdMap = new Map<number, number>();
    for (const g of payload.groups) {
      const oldId = g.id;
      const { id: _omit, ...rest } = g as TargetGroup & { id?: number };
      const remappedTemplateId =
        rest.postTemplateId != null ? templateIdMap.get(rest.postTemplateId) : undefined;
      const newId = await db.groups.add({
        ...rest,
        postTemplateId: remappedTemplateId,
        createdAt: coerceDate(rest.createdAt),
      } as TargetGroup);
      if (oldId != null) groupIdMap.set(oldId, newId);
    }

    // --- drafts ---
    for (const d of payload.drafts) {
      const { id: _omit, ...rest } = d as PostDraft & { id?: number };
      const remappedTargets = (rest.targetGroupIds ?? [])
        .map((oid) => groupIdMap.get(oid))
        .filter((v): v is number => v != null);
      await db.drafts.add({
        ...rest,
        targetGroupIds: remappedTargets,
        createdAt: coerceDate(rest.createdAt),
        updatedAt: coerceDate(rest.updatedAt),
      } as PostDraft);
    }

    return {
      templates: payload.templates.length,
      groups: payload.groups.length,
      drafts: payload.drafts.length,
    };
  });
}

// ---------------------------------------------------------------------------
// Shape validation — the JSON can be hand-edited or come from the desktop
// build, so we check the shape defensively rather than casting blindly.
// ---------------------------------------------------------------------------

function assertExportFile(raw: unknown): ExportFile {
  if (typeof raw !== 'object' || raw === null) {
    throw new Error('Expected a JSON object at the top level.');
  }
  const r = raw as Record<string, unknown>;
  if (r.app !== 'vk-postman') {
    throw new Error(`Not a VK Postman export (app field = ${JSON.stringify(r.app)}).`);
  }
  const version = Number(r.formatVersion ?? 0);
  if (!Number.isFinite(version) || version < 1 || version > FORMAT_VERSION) {
    throw new Error(
      `Unsupported formatVersion ${r.formatVersion}. This build understands up to ${FORMAT_VERSION}.`
    );
  }
  const templates = assertArray<PostTemplate>(r.templates, 'templates', assertTemplate);
  const groups = assertArray<TargetGroup>(r.groups, 'groups', assertGroup);
  const drafts = assertArray<PostDraft>(r.drafts, 'drafts', assertDraft);
  return {
    formatVersion: FORMAT_VERSION,
    exportedAt: String(r.exportedAt ?? new Date().toISOString()),
    app: 'vk-postman',
    templates,
    groups,
    drafts,
  };
}

function assertArray<T>(v: unknown, field: string, each: (x: unknown, i: number) => T): T[] {
  if (!Array.isArray(v)) throw new Error(`Field "${field}" must be an array.`);
  return v.map((x, i) => each(x, i));
}

function assertTemplate(x: unknown, i: number): PostTemplate {
  if (typeof x !== 'object' || x === null) throw new Error(`templates[${i}] is not an object.`);
  const r = x as Record<string, unknown>;
  return {
    id: asOptionalInt(r.id),
    name: asString(r.name, `templates[${i}].name`),
    description: asStringOrEmpty(r.description),
    bodyTemplate: asString(r.bodyTemplate, `templates[${i}].bodyTemplate`),
    defaultThemeTags: asStringArray(r.defaultThemeTags),
    placeholderSchema: asArray(r.placeholderSchema).map((p, j) => assertPlaceholder(p, i, j)),
    createdAt: coerceDate(r.createdAt),
    updatedAt: coerceDate(r.updatedAt),
  };
}

function assertPlaceholder(x: unknown, i: number, j: number): PlaceholderDefinition {
  if (typeof x !== 'object' || x === null)
    throw new Error(`templates[${i}].placeholderSchema[${j}] is not an object.`);
  const r = x as Record<string, unknown>;
  return {
    key: asString(r.key, `templates[${i}].placeholderSchema[${j}].key`),
    displayName: asString(r.displayName, `templates[${i}].placeholderSchema[${j}].displayName`),
    isRequired: Boolean(r.isRequired),
    type: Number(r.type ?? 0),
    description: r.description == null ? undefined : String(r.description),
    defaultValue: r.defaultValue == null ? undefined : String(r.defaultValue),
  };
}

function assertGroup(x: unknown, i: number): TargetGroup {
  if (typeof x !== 'object' || x === null) throw new Error(`groups[${i}] is not an object.`);
  const r = x as Record<string, unknown>;
  return {
    id: asOptionalInt(r.id),
    screenName: asString(r.screenName, `groups[${i}].screenName`).replace(/^@/, ''),
    displayName: asString(r.displayName, `groups[${i}].displayName`),
    mandatoryTags: asStringArray(r.mandatoryTags),
    postTemplateId: asOptionalInt(r.postTemplateId),
    isActive: r.isActive == null ? true : Boolean(r.isActive),
    notes: asStringOrEmpty(r.notes),
    createdAt: coerceDate(r.createdAt),
  };
}

function assertDraft(x: unknown, i: number): PostDraft {
  if (typeof x !== 'object' || x === null) throw new Error(`drafts[${i}] is not an object.`);
  const r = x as Record<string, unknown>;
  return {
    id: asOptionalInt(r.id),
    title: asString(r.title, `drafts[${i}].title`),
    commonText: asStringOrEmpty(r.commonText),
    placeholderValues: asStringDict(r.placeholderValues),
    themeTags: asStringArray(r.themeTags),
    targetGroupIds: asArray(r.targetGroupIds).map((v) => Number(v)),
    createdAt: coerceDate(r.createdAt),
    updatedAt: coerceDate(r.updatedAt),
  };
}

// --- tiny type coercions ----------------------------------------------------

function asString(v: unknown, label: string): string {
  if (typeof v !== 'string') throw new Error(`${label} must be a string.`);
  return v;
}
function asStringOrEmpty(v: unknown): string {
  return typeof v === 'string' ? v : '';
}
function asOptionalInt(v: unknown): number | undefined {
  if (v == null) return undefined;
  const n = Number(v);
  return Number.isFinite(n) ? n : undefined;
}
function asArray(v: unknown): unknown[] {
  return Array.isArray(v) ? v : [];
}
function asStringArray(v: unknown): string[] {
  return asArray(v).map((x) => String(x));
}
function asStringDict(v: unknown): Record<string, string> {
  if (typeof v !== 'object' || v === null) return {};
  const out: Record<string, string> = {};
  for (const [k, val] of Object.entries(v)) out[k] = String(val);
  return out;
}
function coerceDate(v: unknown): Date {
  if (v instanceof Date) return v;
  if (typeof v === 'string' || typeof v === 'number') {
    const d = new Date(v);
    if (!Number.isNaN(d.getTime())) return d;
  }
  return new Date();
}
