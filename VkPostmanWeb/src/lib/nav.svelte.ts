// Cross-view navigation state. A `.svelte.ts` module so it can hold runes and
// be shared across components — App.svelte reads `tab`, and any view can ask
// to jump to a specific record (e.g. Drafts → open a template in Templates).

export type Tab = 'drafts' | 'templates' | 'placeholders' | 'groups' | 'stats';

class Nav {
  /** The active top-level tab. App.svelte renders the matching view. */
  tab = $state<Tab>('drafts');

  /**
   * When non-null, TemplatesView should open this template id and clear the
   * value. Set by `openTemplate()`.
   */
  requestedTemplateId = $state<number | null>(null);

  /** Switch to the Templates tab and ask it to open a specific template. */
  openTemplate(id: number) {
    this.requestedTemplateId = id;
    this.tab = 'templates';
  }
}

export const nav = new Nav();
