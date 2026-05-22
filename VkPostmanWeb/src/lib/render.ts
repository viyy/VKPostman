import {
  PlaceholderType,
  WIKI_LINK_SEPARATOR,
  type PlaceholderDefinition,
  type PostDraft,
  type PostTemplate,
  type TargetGroup,
} from './types';

// ---------------------------------------------------------------------------
// VK link normalizer (parallels C# VkLinkNormalizer).
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
// WikiLink packing — two fields in one string slot.
// ---------------------------------------------------------------------------

export function packWikiLink(target: string, display: string): string {
  return `${target ?? ''}${WIKI_LINK_SEPARATOR}${display ?? ''}`;
}

export function splitWikiLink(packed: string | undefined): { target: string; display: string } {
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

export function renderPlaceholder(def: PlaceholderDefinition, value: string | undefined): string {
  if (!value || !value.trim()) return def.defaultValue ?? '';
  switch (def.type) {
    case PlaceholderType.VkLink:   return normalizeVkLink(value);
    case PlaceholderType.WikiLink: return renderWikiLink(value);
    case PlaceholderType.TagList:  return normalizeTagList(value);
    case PlaceholderType.Text:
    case PlaceholderType.Url:
    default:
      return value;
  }
}

// ---------------------------------------------------------------------------
// Built-in placeholders (set by the renderer; never live in the library).
// ---------------------------------------------------------------------------

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

/** Control keywords that look like placeholders in {{ … }} but aren't. */
const RESERVED_KEYS = new Set(['if', 'unless', 'else']);

export function extractPlaceholderKeys(body: string): string[] {
  if (!body) return [];
  const seen = new Set<string>();
  const result: string[] = [];
  const add = (key: string) => {
    if (RESERVED_KEYS.has(key) || seen.has(key)) return;
    seen.add(key);
    result.push(key);
  };

  // Plain {{ key }} substitutions.
  const re = /\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(body)) !== null) add(m[1]);

  // Keys referenced only in a condition: {{#if key}} / {{#unless key}}.
  const cond = /\{\{\s*#(?:if|unless)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\}\}/g;
  while ((m = cond.exec(body)) !== null) add(m[1]);

  return result;
}

export function extractLibraryPlaceholderKeys(body: string): string[] {
  return extractPlaceholderKeys(body).filter((k) => !isBuiltInPlaceholder(k));
}

// ---------------------------------------------------------------------------
// Template engine — minimalist `{{ name }}` substitution.
// ---------------------------------------------------------------------------

const EXPR_RE = /\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}/g;

// Innermost {{#if key}}…{{/if}} or {{#unless key}}…{{/unless}} block (optionally
// with {{else}}). The negative lookahead keeps the match innermost so nested
// blocks resolve from the inside out across repeated passes.
const COND_RE =
  /\{\{\s*#(if|unless)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\}\}((?:(?!\{\{\s*#(?:if|unless)\b)[\s\S])*?)\{\{\s*\/\1\s*\}\}/;

const isTruthy = (ctx: Record<string, string>, key: string): boolean =>
  typeof ctx[key] === 'string' && ctx[key].trim().length > 0;

/** Resolve conditional blocks against ctx, then substitute {{ key }} values. */
export function renderTemplate(body: string, ctx: Record<string, string>): string {
  let out = body;
  // Resolve conditionals first (bounded loop guards against pathological input).
  for (let i = 0; i < 1000; i++) {
    const m = COND_RE.exec(out);
    if (!m) break;
    const [full, kind, key, inner] = m;
    const [whenTrue = '', whenFalse = ''] = inner.split(/\{\{\s*else\s*\}\}/);
    const cond = kind === 'unless' ? !isTruthy(ctx, key) : isTruthy(ctx, key);
    out = out.slice(0, m.index) + (cond ? whenTrue : whenFalse) + out.slice(m.index + full.length);
  }
  return out.replace(EXPR_RE, (_, key: string) => ctx[key] ?? '');
}

// ---------------------------------------------------------------------------
// Full draft-for-group render — mirrors C# PostDraft.RenderForGroup.
// ---------------------------------------------------------------------------

const hashify = (t: string) => (t.startsWith('#') ? t : `#${t}`);

export function renderDraftForGroup(
  draft: PostDraft,
  group: TargetGroup,
  template: PostTemplate,
  library: Map<string, PlaceholderDefinition>,
): string {
  const ctx: Record<string, string> = {
    group_name: group.displayName,
    group_tags: (group.mandatoryTags ?? []).map(hashify).join(' '),
    theme_tags: (draft.themeTags ?? []).map(hashify).join(' '),
    common_text: draft.commonText ?? '',
  };

  for (const key of extractLibraryPlaceholderKeys(template.bodyTemplate)) {
    const rawValue = draft.placeholderValues[key] ?? '';
    const def = library.get(key);
    ctx[key] = def ? renderPlaceholder(def, rawValue) : rawValue;
  }

  return renderTemplate(template.bodyTemplate, ctx);
}

// ---------------------------------------------------------------------------
// Live-preview rendering for the template editor: fill the body with plausible
// sample values so the author can see the shape of a real post.
// ---------------------------------------------------------------------------

/** A representative sample value for a placeholder, used only in previews. */
export function sampleValueFor(def: PlaceholderDefinition | undefined, key: string): string {
  if (def?.defaultValue && def.defaultValue.trim()) return def.defaultValue;
  switch (def?.type) {
    case PlaceholderType.VkLink:   return '@example';
    case PlaceholderType.WikiLink: return '[example|Example Page]';
    case PlaceholderType.Url:      return 'https://example.com';
    case PlaceholderType.TagList:  return '#sample #tags';
    default:                       return def?.displayName ?? key;
  }
}

/**
 * Render a template body with sample data for the editor preview. Built-ins get
 * fixed sample text; library keys get `sampleValueFor`. `themeTags` (the
 * template's defaults) are used for {{ theme_tags }} when present.
 */
export function renderTemplatePreview(
  body: string,
  library: Map<string, PlaceholderDefinition>,
  themeTags: string[] = [],
): string {
  const ctx: Record<string, string> = {
    group_name:  'Sample Group',
    group_tags:  '#group_tag',
    theme_tags:  (themeTags.length ? themeTags : ['theme']).map(hashify).join(' '),
    common_text: 'Sample common text from the draft.',
  };
  for (const key of extractLibraryPlaceholderKeys(body)) {
    ctx[key] = sampleValueFor(library.get(key), key);
  }
  return renderTemplate(body, ctx);
}

// ---------------------------------------------------------------------------
// Union of placeholder keys across the selected groups' templates.
// ---------------------------------------------------------------------------

export interface PlaceholderUsage {
  key: string;
  definition: PlaceholderDefinition | undefined;
  usedByGroups: string[];
}

export function unionedPlaceholders(
  groups: TargetGroup[],
  templatesById: Map<number, PostTemplate>,
  library: Map<string, PlaceholderDefinition>,
): PlaceholderUsage[] {
  const byKey = new Map<string, PlaceholderUsage>();

  for (const g of groups) {
    if (g.postTemplateId == null) continue;
    const tpl = templatesById.get(g.postTemplateId);
    if (!tpl) continue;

    for (const key of extractLibraryPlaceholderKeys(tpl.bodyTemplate)) {
      const existing = byKey.get(key);
      if (!existing) {
        byKey.set(key, {
          key,
          definition: library.get(key),
          usedByGroups: [g.displayName],
        });
      } else {
        existing.usedByGroups.push(g.displayName);
      }
    }
  }

  return [...byKey.values()].sort((a, b) =>
    (a.definition?.displayName ?? a.key).localeCompare(b.definition?.displayName ?? b.key),
  );
}

export function isDraftReady(
  _draft: PostDraft,
  selectedGroups: TargetGroup[],
): boolean {
  if (selectedGroups.length === 0) return false;
  if (selectedGroups.some((g) => g.postTemplateId == null)) return false;
  return true;
}
