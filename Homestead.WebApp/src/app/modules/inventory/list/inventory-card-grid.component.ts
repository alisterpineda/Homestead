import { Component, ChangeDetectionStrategy, computed, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMapPin, lucideTriangleAlert } from '@ng-icons/lucide';
import { HlmCardImports } from '@/shared/components/spartan/card';
import type { ColumnVisibilityState } from '@tanstack/angular-table';
import { TagBadgeComponent } from '@/shared/components/tag-badge/tag-badge.component';
import { formatRelativeAge } from '@/shared/utils/relative-age';
import { InventoryRowActionsComponent } from './inventory-row-actions.component';
import { ItemStatusBadgeComponent } from '../shared/item-status-badge.component';
import { LocationPathComponent } from '../shared/location-path.component';
import { isTerminalStatus, LOW_STOCK_BADGE_CLASS } from '../shared/item-status-meta';
import { formatQty } from '../consumables/consumable-shared';
import { UnitPipe } from '../consumables/unit.pipe';
import { locationCellView } from './inventory-row';
import type { InventoryRow, LocationCellView } from './inventory-row';

const CARD_BASE_CLASSES =
  'flex flex-col gap-1.5 p-3 cursor-pointer transition-colors hover:bg-accent/50 ' +
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring';

interface CardView {
  readonly row: InventoryRow;
  readonly location: LocationCellView;
  readonly terminal: boolean;
}

/**
 * The card rendering of the unified inventory list — the same {@link InventoryRow}s the table draws,
 * for narrow viewports and the card view toggle. Every rule that decides *what* a row says is shared
 * with the table rather than re-derived here: the Location branch ({@link locationCellView}), the
 * terminal-status muting ({@link isTerminalStatus}), the quantity/unit/low-stock formatting. Only the
 * layout differs.
 *
 * The whole card is the click target (it opens the quick panel), so it carries `role="button"` and a
 * keyboard path; the `⋮` menu stops its own propagation ({@link InventoryRowActionsComponent}) and its
 * items render in an overlay, so neither needs a guard here.
 */
@Component({
  selector: 'app-inventory-card-grid',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmCardImports,
    TagBadgeComponent,
    ItemStatusBadgeComponent,
    LocationPathComponent,
    InventoryRowActionsComponent,
    UnitPipe,
  ],
  providers: [provideIcons({ lucideMapPin, lucideTriangleAlert })],
  template: `
    <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
      @for (card of cards(); track card.row.id) {
        @let row = card.row;
        <div
          hlmCard
          role="button"
          tabindex="0"
          [class]="card.terminal ? terminalCardClass : cardClass"
          (click)="open.emit(row)"
          (keydown.enter)="open.emit(row)"
          (keydown.space)="onSpace($event, row)"
        >
          <div class="flex items-start gap-2">
            <div class="min-w-0 flex-1">
              <span class="block truncate text-sm font-medium">{{ row.name }}</span>
              @if (row.durable?.serial) {
                <span class="block truncate text-xs text-muted-foreground">{{ row.durable.serial }}</span>
              }
            </div>
            <!-- Cards are not a dense column of rows, so the resting Active state stays unbadged.
                 The note (borrower / archive reason) rides along with the pill. -->
            <app-item-status-badge [status]="row.status" [note]="row.statusNote" class="shrink-0 max-w-[50%]" />
            <app-inventory-row-actions
              class="-mr-1 -mt-1 shrink-0"
              [row]="row"
              (edit)="edit.emit($event)"
              (viewContents)="viewContents.emit($event)"
              (viewDetails)="viewDetails.emit($event)"
              (archiveRestore)="archiveRestore.emit($event)"
              (delete)="delete.emit($event)"
            />
          </div>

          @if (row.tags.length) {
            <div class="flex flex-wrap gap-1">
              @for (t of row.tags; track t.id) {
                <app-tag-badge [path]="t.path" [color]="t.effectiveColor" [compact]="true" />
              }
            </div>
          }

          @if (showLocation()) {
            @let loc = card.location;
            <div class="flex min-w-0 items-center gap-1.5 text-sm">
              <ng-icon name="lucideMapPin" class="text-[length:--spacing(3)] shrink-0 text-muted-foreground" />
              @switch (loc.kind) {
                @case ('path') {
                  <!-- Narrow by construction, so the middle collapses; the tooltip keeps the full path. -->
                  <app-location-path [path]="loc.view" />
                }
                @case ('placeholder') {
                  <span class="italic text-muted-foreground">{{ loc.text }}</span>
                }
                @default {
                  <span class="truncate" [title]="loc.text">{{ loc.text }}</span>
                }
              }
            </div>
          }

          <div class="flex items-center gap-2 text-xs text-muted-foreground">
            @if (row.kind === 'consumable') {
              <!-- The quantity is the card's one emphasised number — except on a terminal row, which
                   mutes uniformly and so keeps the footer's inherited colour. -->
              <span class="text-sm tabular-nums" [class.text-foreground]="!card.terminal">
                {{ formatQty(row.onHand ?? 0) }}
              </span>
              <span>{{ row.unit | unit }}</span>
              @if (row.onHandHint) {
                <span>{{ row.onHandHint }}</span>
              }
              @if (row.isLow) {
                <span [class]="lowStockBadgeClass">
                  <ng-icon name="lucideTriangleAlert" class="text-[length:--spacing(3)]" />
                  Low
                </span>
              }
            }
            <span class="ml-auto tabular-nums">{{ formatRelativeAge(row.updatedAt) }}</span>
          </div>
        </div>
      }
    </div>
  `,
})
export class InventoryCardGridComponent {
  rows = input.required<InventoryRow[]>();
  /**
   * Per-column visibility, shared verbatim with the table (TanStack {@link ColumnVisibilityState}) so one
   * parent preference drives both renderers. Only `location` is meaningful on a card — the other
   * lines are intrinsic to the card's shape, not columns a user can turn off.
   */
  columnVisibility = input<ColumnVisibilityState>({});

  open = output<InventoryRow>();
  edit = output<InventoryRow>();
  viewContents = output<InventoryRow>();
  viewDetails = output<InventoryRow>();
  archiveRestore = output<InventoryRow>();
  delete = output<InventoryRow>();

  /** Absent key = shown, matching TanStack's own default for an empty {@link ColumnVisibilityState}. */
  protected showLocation = computed(() => this.columnVisibility()['location'] !== false);

  /**
   * Per-row derived state, computed once per `rows()` change rather than per change-detection pass.
   * {@link locationCellView} allocates a fresh path view on every call; deriving it here keeps the
   * object handed to `<app-location-path>` referentially stable, so that OnPush child is skipped
   * on the card clicks/keystrokes that would otherwise re-render every card for no visible change.
   */
  protected cards = computed<readonly CardView[]>(() =>
    this.rows().map(row => ({
      row,
      location: locationCellView(row),
      terminal: isTerminalStatus(row.status),
    })));

  protected readonly lowStockBadgeClass = LOW_STOCK_BADGE_CLASS;
  protected readonly formatQty = formatQty;
  protected readonly formatRelativeAge = formatRelativeAge;
  protected readonly cardClass = CARD_BASE_CLASSES;
  protected readonly terminalCardClass = `${CARD_BASE_CLASSES} italic text-muted-foreground`;

  protected onSpace(event: Event, row: InventoryRow): void {
    event.preventDefault(); // keep Space from scrolling the page
    this.open.emit(row);
  }
}
