// ---------------------------------------------------------------------------
// Domain types — kept deliberately parallel to the C# side (Core/Models/*).
// PlaceholderDefinition is now a first-class entity in its own store, referenced
// by key from template bodies. The per-template `placeholderSchema` is gone.
// ---------------------------------------------------------------------------

export enum PlaceholderType {
  /** Arbitrary text; rendered as-is. */
  Text = 0,
  /** VK user or community; input forms like `nelfias` / `@nelfias` / `vk.com/nelfias` → rendered as `@nelfias`. */
  VkLink = 1,
  /** http(s) URL; rendered as-is. */
  Url = 2,
  /** Space-separated tags; rendered as `#tag1 #tag2 …` (adds `#` where missing). */
  TagList = 3,
  /** VK wiki mention with two inputs; rendered as `[target|display]`. */
  WikiLink = 4,
}

export interface PlaceholderDefinition {
  id?: number;
  /** Identifier referenced in a template body as `{{ key }}`. Unique across the library. */
  key: string;
  displayName: string;
  type: PlaceholderType;
  description?: string;
  defaultValue?: string;
  createdAt: Date;
  updatedAt: Date;
}

export interface PostTemplate {
  id?: number;
  name: string;
  description: string;
  bodyTemplate: string;
  defaultThemeTags: string[];
  createdAt: Date;
  updatedAt: Date;
}

export interface TargetGroup {
  id?: number;
  screenName: string;        // without leading @
  displayName: string;
  mandatoryTags: string[];
  postTemplateId?: number;   // nullable — groups can exist without a template
  isActive: boolean;
  notes: string;
  createdAt: Date;
}

export interface PostDraft {
  id?: number;
  title: string;
  commonText: string;
  /** Keyed by PlaceholderDefinition.key. */
  placeholderValues: Record<string, string>;
  /** Tags common to every selected group; prepended by renderer. */
  themeTags: string[];
  targetGroupIds: number[];
  createdAt: Date;
  updatedAt: Date;
}

export const WIKI_LINK_SEPARATOR = '\u001F';
