import {
  PlaceholderType,
  WIKI_LINK_SEPARATOR,
  type PlaceholderDefinition,
  type PostDraft,
  type PostTemplate,
  type TargetGroup,
} from './types';

// ---------------------------------------------------------------------------
// VK link normalizer — ports VkLinkNormalizer from the C# side.
// Accepts: `nelfias` | `@nelfias` | `vk.com/nelfias` | `https://vk.com/nelfias`
// Emits:  `@nelfias`   (leading @ included; strip it yourself if unwanted)
// ---------------------------------------------------------------------------

const VK_HOST_RE = /^(https?:\/\/)?(www\.|m\.)?(vk\.com|vk\.ru)\//i;
const SHORTNAME_RE = /^[A-Za-z_][A-Za-z0-9_.]{0,63}$/;

export function normalizeVkLink(input: string | null | undefined): string {
  if (!input) return '';
  let s = input.trim();

  const hostMatch = VK_HOST_RE.exec(s);
  if (hostMatch) s = s.slice(hostMatch[0].length);

  if (s.startsWith('@')) s = s.slice(1);

  const tail = s.search(/[/?#]/);
  if (tail >= 0) s = s.slice(0, tail);

  return s ? '@' + s : '';
}

export function looksLikeVkLink(input: string | null | undefined): boolean {
  const n = normalizeVkLink(input);
  return n.length > 1 && SHORTNAME_RE.test(n.slice(1));
}

// ---------------------------------------------------------------------------
// WikiLink packing — we pack (target, display) into a single string because the
// draft's placeholderValues is a flat Record<string, string>. Matches C#.
// ---------------------------------------------------------------------------

export function packWikiLink(target: string, display: string): string {
  return `${target ?? ''}${WIKI_LINK_SEPARATOR}${display ?? ''}`;
}

export function splitWikiLink(packed: string | undefined): {
  target: string;
  display: string;
} {
  if (!packed) return { target: '', display: '' };
  const idx = packed.indexOf(WIKI_LINK_SEPARATOR);
  if (idx < 0) return { target: packed, display: '' };
  return { target: packed.slice(0, idx), display: packed.slice(idx + 1) };
}

function renderWikiLink(packed: string): string {
  const { target: rawTarget, display } = splitWikiLink(packed);
  if (!rawTarget.trim()) return '';
  const target = normalizeVkLink(rawTarget).replace(/^@/, '');
  const label = display.trim() || target;
  return `[${target}|${label}]`;
}

function normalizeTagList(value: string): string {
  return value
    .split(' ')
    .filter((t) => t.length > 0)
    .map((t) => (t.startsWith('#') ? t : `#${t}`))
    .join(' ');
}

// ---------------------------------------------------------------------------
// Per-type rendering of a placeholder's raw string into its output string.
// ---------------------------------------------------------------------------

export function renderPlaceholder(def: PlaceholderDefinition, value: string | undefined): string {
  if (!value || !value.trim()) return def.defaultValue ?? '';
  switch (def.type) {
    case PlaceholderType.VkLink:
      return normalizeVkLink(value);
    case PlaceholderType.WikiLink:
      return renderWikiLink(value);
    case PlaceholderType.TagList:
      return normalizeTagList(value);
    case PlaceholderType.Text:
    case PlaceholderType.Url:
    default:
      return value;
  }
}

// ---------------------------------------------------------------------------
// Template engine — minimalist Scriban-compatible `{{ name }}` substitution.
// No loops or conditionals; our templates don't use them.
// ---------------------------------------------------------------------------

const EXPR_RE = /\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}/g;

/**
 * Placeholder keys the renderer injects automatically for every group.
 * These should never auto-appear in a template's PlaceholderSchema because
 * they come from the draft / group, not from user-fillable inputs.
 */
export const BUILT_IN_PLACEHOLDERS = [
  'common_text',
  'group_tags',
  'theme_tags',
  'group_name',
] as const;

export type BuiltInPlaceholder = (typeof BUILT_IN_PLACEHOLDERS)[number];

export function isBuiltInPlaceholder(key: string): boolean {
  return (BUILT_IN_PLACEHOLDERS as readonly string[]).includes(key);
}

/**
 * Returns the unique set of placeholder keys referenced in a template body,
 * in first-appearance order. Used by the editor to auto-add schema entries
 * for keys the user has typed into the Body field.
 */
export function extractPlaceholderKeys(body: string): string[] {
  if (!body) return [];
  const seen = new Set<string>();
  const result: string[] = [];
  // A fresh regex per call because EXPR_RE has the /g flag and carries state.
  const re = /\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(body)) !== null) {
    const key = m[1];
    if (!seen.has(key)) {
      seen.add(key);
      result.push(key);
    }
  }
  return result;
}

export function renderTemplate(body: string, ctx: Record<string, string>): string {
  return body.replace(EXPR_RE, (_, key: string) => (ctx[key] ?? ''));
}

// ---------------------------------------------------------------------------
// Full draft-for-group render — mirrors C# PostDraft.RenderForGroup.
// ---------------------------------------------------------------------------

const hashify = (t: string) => (t.startsWith('#') ? t : `#${t}`);

export function renderDraftForGroup(
  draft: PostDraft,
  group: TargetGroup,
  template: PostTemplate
): string {
  const placeholderOutputs: Record<string, string> = {};
  for (const def of template.placeholderSchema) {
    placeholderOutputs[def.key] = renderPlaceholder(def, draft.placeholderValues[def.key]);
  }

  const ctx: Record<string, string> = {
    ...placeholderOutputs,
    group_name: group.displayName,
    group_tags: (group.mandatoryTags ?? []).map(hashify).join(' '),
    theme_tags: (draft.themeTags ?? []).map(hashify).join(' '),
    common_text: draft.commonText ?? '',
  };

  return renderTemplate(template.bodyTemplate, ctx);
}

// ---------------------------------------------------------------------------
// Union of placeholder definitions across the selected groups' templates.
// Stricter required-ness wins when two templates disagree on the same key.
// ---------------------------------------------------------------------------

export interface PlaceholderUsage {
  definition: PlaceholderDefinition;
  usedByGroups: string[];
}

export function unionedPlaceholders(
  groups: TargetGroup[],
  templatesById: Map<number, PostTemplate>
): PlaceholderUsage[] {
  const byKey = new Map<string, PlaceholderUsage>();

  for (const g of groups) {
    if (g.postTemplateId == null) continue;
    const tpl = templatesById.get(g.postTemplateId);
    if (!tpl) continue;

    for (const def of tpl.placeholderSchema) {
      const existing = byKey.get(def.key);
      if (!existing) {
        byKey.set(def.key, { definition: { ...def }, usedByGroups: [g.displayName] });
        continue;
      }
      // Stricter required-ness wins.
      if (def.isRequired && !existing.definition.isRequired) {
        existing.definition.isRequired = true;
      }
      existing.usedByGroups.push(g.displayName);
    }
  }

  return [...byKey.values()].sort((a, b) =>
    a.definition.displayName.localeCompare(b.definition.displayName)
  );
}

export function isDraftReady(
  draft: PostDraft,
  selectedGroups: TargetGroup[],
  templatesById: Map<number, PostTemplate>
): boolean {
  if (selectedGroups.length === 0) return false;
  if (selectedGroups.some((g) => g.postTemplateId == null)) return false;

  const required = new Set<string>();
  for (const g of selectedGroups) {
    const tpl = templatesById.get(g.postTemplateId!);
    if (!tpl) return false;
    for (const def of tpl.placeholderSchema) {
      if (def.isRequired) required.add(def.key);
    }
  }
  for (const key of required) {
    const v = draft.placeholderValues[key];
    if (!v || !v.trim()) return false;
  }
  return true;
}
