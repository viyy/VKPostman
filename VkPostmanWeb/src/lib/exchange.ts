// ---------------------------------------------------------------------------
// JSON export / import for interchange between the desktop (WPF) build and
// this PWA. File shape (v2):
//
//   {
//     "formatVersion": 2,
//     "exportedAt": "2026-04-17T12:34:56.000Z",
//     "app": "vk-postman",
//     "placeholders": [ ... ],
//     "templates":    [ ... ],
//     "groups":       [ ... ],
//     "drafts":       [ ... ]
//   }
//
// Import strategy: REPLACE. Clear all four tables, re-insert with **remapped
// IDs** so cross-references (group.postTemplateId, draft.targetGroupIds) stay
// consistent regardless of source IDs. Backward-compat: v1 files had no
// placeholders array and inline per-template `placeholderSchema` — we flatten
// those into the new library on the way in.
// ---------------------------------------------------------------------------

import { db } from './db';
import {
  PlaceholderType,
  type PlaceholderDefinition,
  type PostDraft,
  type PostTemplate,
  type TargetGroup,
} from './types';

export const FORMAT_VERSION = 2;

export interface ExportFile {
  formatVersion: number;
  exportedAt: string;
  app: 'vk-postman';
  placeholders: PlaceholderDefinition[];
  templates: PostTemplate[];
  groups: TargetGroup[];
  drafts: PostDraft[];
}

export async function exportAll(): Promise<ExportFile> {
  const [placeholders, templates, groups, drafts] = await Promise.all([
    db.placeholders.toArray(),
    db.templates.toArray(),
    db.groups.toArray(),
    db.drafts.toArray(),
  ]);
  return {
    formatVersion: FORMAT_VERSION,
    exportedAt: new Date().toISOString(),
    app: 'vk-postman',
    placeholders,
    templates,
    groups,
    drafts,
  };
}

export function toJsonBlob(data: ExportFile): Blob {
  return new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
}

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
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export interface ImportSummary {
  placeholders: number;
  templates: number;
  groups: number;
  drafts: number;
}

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

export async function importFromJson(parsed: unknown): Promise<ImportSummary> {
  const payload = assertExportFile(parsed);

  return db.transaction(
    'rw',
    [db.templates, db.groups, db.drafts, db.placeholders],
    async () => {
      await Promise.all([
        db.templates.clear(),
        db.groups.clear(),
        db.drafts.clear(),
        db.placeholders.clear(),
      ]);

      // --- placeholders ---
      for (const p of payload.placeholders) {
        const { id: _omit, ...rest } = p as PlaceholderDefinition & { id?: number };
        await db.placeholders.add({
          ...rest,
          createdAt: coerceDate(rest.createdAt),
          updatedAt: coerceDate(rest.updatedAt),
        } as PlaceholderDefinition);
      }

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
        placeholders: payload.placeholders.length,
        templates: payload.templates.length,
        groups: payload.groups.length,
        drafts: payload.drafts.length,
      };
    },
  );
}

// ---------------------------------------------------------------------------
// Shape validation + v1 → v2 migration
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
      `Unsupported formatVersion ${r.formatVersion}. This build understands 1..${FORMAT_VERSION}.`,
    );
  }

  // v1 → v2 migration: collect every inline placeholderSchema entry into
  // a top-level placeholders array (first-write-wins on key conflicts).
  if (version === 1) {
    upconvertV1(r);
  }

  const placeholders = assertArray(r.placeholders, 'placeholders', assertPlaceholder);
  const templates    = assertArray(r.templates,    'templates',    assertTemplate);
  const groups       = assertArray(r.groups,       'groups',       assertGroup);
  const drafts       = assertArray(r.drafts,       'drafts',       assertDraft);

  return {
    formatVersion: FORMAT_VERSION,
    exportedAt: String(r.exportedAt ?? new Date().toISOString()),
    app: 'vk-postman',
    placeholders,
    templates,
    groups,
    drafts,
  };
}

function upconvertV1(raw: Record<string, unknown>): void {
  const library = new Map<string, Record<string, unknown>>();
  let id = 1;
  const tpls = Array.isArray(raw.templates) ? raw.templates : [];
  for (const t of tpls as Array<Record<string, unknown>>) {
    const schema = Array.isArray(t.placeholderSchema) ? t.placeholderSchema : [];
    for (const def of schema as Array<Record<string, unknown>>) {
      const key = typeof def?.key === 'string' ? def.key : null;
      if (!key || library.has(key)) continue;
      library.set(key, {
        id: id++,
        key,
        displayName: typeof def.displayName === 'string' ? def.displayName : key,
        type: typeof def.type === 'number' ? def.type : 0,
        description: typeof def.description === 'string' ? def.description : undefined,
        defaultValue: typeof def.defaultValue === 'string' ? def.defaultValue : undefined,
      });
    }
    delete t.placeholderSchema;
  }
  raw.placeholders = [...library.values()];
  raw.formatVersion = FORMAT_VERSION;
}

function assertArray<T>(v: unknown, field: string, each: (x: unknown, i: number) => T): T[] {
  if (v == null) return []; // Tolerate missing optional arrays from older exports.
  if (!Array.isArray(v)) throw new Error(`Field "${field}" must be an array.`);
  return v.map((x, i) => each(x, i));
}

function assertPlaceholder(x: unknown, i: number): PlaceholderDefinition {
  if (typeof x !== 'object' || x === null) throw new Error(`placeholders[${i}] is not an object.`);
  const r = x as Record<string, unknown>;
  return {
    id: asOptionalInt(r.id),
    key: asString(r.key, `placeholders[${i}].key`),
    displayName: asString(r.displayName, `placeholders[${i}].displayName`),
    type: coerceType(r.type),
    description: r.description == null ? undefined : String(r.description),
    defaultValue: r.defaultValue == null ? undefined : String(r.defaultValue),
    createdAt: coerceDate(r.createdAt),
    updatedAt: coerceDate(r.updatedAt),
  };
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
    createdAt: coerceDate(r.createdAt),
    updatedAt: coerceDate(r.updatedAt),
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

// ---- tiny coercions --------------------------------------------------------

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
function coerceType(v: unknown): PlaceholderType {
  const n = Number(v ?? 0);
  return Number.isFinite(n) ? (n as PlaceholderType) : PlaceholderType.Text;
}
