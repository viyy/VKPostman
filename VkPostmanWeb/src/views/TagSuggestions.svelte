<script lang="ts">
  // Chip row of previously-used tags. Clicking a chip appends it to the bound
  // input value. Tags already present in `current` are hidden.
  interface Props {
    tags: string[];
    current: string;
    onpick: (tag: string) => void;
  }
  let { tags, current, onpick }: Props = $props();

  const present = $derived(
    new Set(
      current
        .split(/\s+/)
        .map((t) => t.replace(/^#+/, '').trim().toLowerCase())
        .filter(Boolean),
    ),
  );
  const suggestions = $derived(tags.filter((t) => !present.has(t.toLowerCase())));
</script>

{#if suggestions.length > 0}
  <div class="placeholder-chiprow">
    <span class="muted" style="font-size: 0.75rem;">Add:</span>
    {#each suggestions as tag (tag)}
      <button type="button" class="chip chip-global" onclick={() => onpick(tag)}>#{tag}</button>
    {/each}
  </div>
{/if}
