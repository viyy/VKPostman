// Cross-view navigation state. A `.svelte.ts` module so it can hold runes and
// be shared across components — App.svelte reads `tab`, and any view can ask
// to jump to a specific record (e.g. Drafts → open a template in Templates).

export type Tab = 'drafts' | 'templates' | 'placeholders' | 'groups' | 'stats';

class Nav {
  /** The active top-level tab. App.svelte renders the matching view. */
  tab = $state<Tab>('drafts');

  /**
   * When non-null, the matching view should open this record and clear the
   * value. Set by the `open*()` helpers (used by cross-tab links + global search).
   */
  requestedTemplateId = $state<number | null>(null);
  requestedDraftId = $state<number | null>(null);
  requestedGroupId = $state<number | null>(null);

  /** Switch to the Templates tab and ask it to open a specific template. */
  openTemplate(id: number) {
    this.requestedTemplateId = id;
    this.tab = 'templates';
  }

  /** Switch to the Drafts tab and ask it to open a specific draft. */
  openDraft(id: number) {
    this.requestedDraftId = id;
    this.tab = 'drafts';
  }

  /** Switch to the Groups tab and ask it to open a specific group. */
  openGroup(id: number) {
    this.requestedGroupId = id;
    this.tab = 'groups';
  }
}

export const nav = new Nav();
