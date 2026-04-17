// ---------------------------------------------------------------------------
// Domain types — kept deliberately parallel to the C# side (Core/Models/*).
// Not identical: we use plain JS objects + IndexedDB, and WikiLink values are
// packed as "target\u001Fdisplay" strings for parity with the desktop encoding.
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
  key: string;
  displayName: string;
  isRequired: boolean;
  type: PlaceholderType;
  description?: string;
  defaultValue?: string;
}

export interface PostTemplate {
  id?: number;
  name: string;
  description: string;
  bodyTemplate: string;
  defaultThemeTags: string[];
  placeholderSchema: PlaceholderDefinition[];
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
  vkGroupId?: number;
  createdAt: Date;
}

export interface PostDraft {
  id?: number;
  title: string;
  commonText: string;
  /** Union of placeholder values across the selected groups' templates. */
  placeholderValues: Record<string, string>;
  /** Tags common to every selected group; prepended by renderer. */
  themeTags: string[];
  targetGroupIds: number[];
  createdAt: Date;
  updatedAt: Date;
}

export const WIKI_LINK_SEPARATOR = '\u001F';
