import { Component, ChangeDetectionStrategy, input, computed } from '@angular/core';
import type { ItemStatus } from '@/api/generated/models/item-status';
import { ITEM_STATUS_META, statusNoteText } from './item-status-meta';

@Component({
  selector: 'app-item-status-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Collapse the host to display:none when suppressed so it stops occupying a flex `gap`
  // slot (the host is itself a flex item in several containers — no phantom gap). The display
  // utility is bound, not static: `.inline-flex` is emitted after `.hidden` at equal specificity,
  // so a static `inline-flex` would win and the host would never actually collapse.
  host: {
    class: 'items-center gap-1.5 min-w-0',
    '[class.inline-flex]': 'showBadge()',
    '[class.hidden]': '!showBadge()',
  },
  template: `
    @if (showBadge()) {
      <span [class]="meta().badgeClass">{{ meta().label }}</span>
      @if (noteText(); as note) {
        <span class="truncate text-xs text-muted-foreground">{{ note }}</span>
      }
    }
  `,
})
export class ItemStatusBadgeComponent {
  status = input.required<ItemStatus>();
  /**
   * The item's status note, rendered as muted text beside the pill — the borrower when Lent, the
   * reason when Archived. Every surface passes it, so the pair renders identically everywhere.
   */
  note = input<string | null | undefined>(null);
  /**
   * Opt in to rendering the default state (Active) too — for dense list/table contexts where a
   * consistent status column is worth a quiet chip on every row. Off everywhere else, so single-item
   * surfaces (detail headers, quick panels) and the Browse strips stay free of redundant Active chips.
   */
  showDefault = input(false);

  protected meta = computed(() => ITEM_STATUS_META[this.status()]);
  // Non-default statuses always show a chip; the default (Active) only when a host opts in.
  protected showBadge = computed(() => this.showDefault() || !this.meta().isDefault);
  protected noteText = computed(() => statusNoteText(this.status(), this.note()));
}
