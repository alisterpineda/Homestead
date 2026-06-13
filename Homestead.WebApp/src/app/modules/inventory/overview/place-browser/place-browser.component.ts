import { Component, ChangeDetectionStrategy, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideMapPin,
  lucideMapPinOff,
  lucideBox,
  lucideChevronRight,
  lucideTriangleAlert,
  lucideCircleAlert,
  lucidePackage,
  lucidePlus,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { PlaceCardComponent } from './place-card.component';
import { formatPlaceParam, type PlaceRef } from './place-param';
import { ItemStatusBadgeComponent } from '../../shared/item-status-badge.component';
import { CreateEntryDialogComponent } from '../../list/create-entry-dialog/create-entry-dialog.component';
import type { ItemLocationValue } from '../../shared/location-picker/location-picker.component';
import { formatQty } from '../../supplies/supply-shared';
import type { GetInventoryOverviewResponse } from '@/api/generated/models/get-inventory-overview-response';
import type { PlaceContentsResponse } from '@/api/generated/models/place-contents-response';
import type { LocationSegmentResponse } from '@/api/generated/models/location-segment-response';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { SupplyResponse } from '@/api/generated/models/supply-response';

/**
 * The overview's browse-by-place section: at root, a card grid of areas (+ Unplaced); drilled in
 * (`place` set), a breadcrumb, drillable child-container cards, and an inline list of the place's
 * direct items/supplies. Presentational — the page owns the URL state and the data fetches; row
 * clicks bubble up so the page can open its quick panels in place.
 */
@Component({
  selector: 'app-place-browser',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmEmptyImports,
    PlaceCardComponent,
    ItemStatusBadgeComponent,
    CreateEntryDialogComponent,
  ],
  providers: [provideIcons({
    lucideMapPin,
    lucideMapPinOff,
    lucideBox,
    lucideChevronRight,
    lucideTriangleAlert,
    lucideCircleAlert,
    lucidePackage,
    lucidePlus,
  })],
  template: `
    @if (place(); as ref) {
      <section class="flex flex-col gap-4">
        <nav class="flex flex-wrap items-center gap-1 text-sm text-muted-foreground" aria-label="Place breadcrumb">
          <a
            [routerLink]="[]"
            [queryParams]="{ place: null }"
            queryParamsHandling="merge"
            class="hover:text-foreground hover:underline"
          >All places</a>
          @for (seg of breadcrumb(); track seg.id; let last = $last) {
            <ng-icon hlm name="lucideChevronRight" size="xs" class="shrink-0" />
            @if (last) {
              <span class="font-medium text-foreground">{{ seg.name }}</span>
            } @else {
              <a
                [routerLink]="[]"
                [queryParams]="segmentParams(seg)"
                queryParamsHandling="merge"
                class="hover:text-foreground hover:underline"
              >{{ seg.name }}</a>
            }
          } @empty {
            @if (contents()) {
              <ng-icon hlm name="lucideChevronRight" size="xs" class="shrink-0" />
              <span class="font-medium text-foreground">{{ placeName() }}</span>
            }
          }
        </nav>

        @if (error()) {
          <div hlmEmpty>
            <div hlmEmptyHeader>
              <div hlmEmptyMedia variant="icon">
                <ng-icon name="lucideCircleAlert" />
              </div>
              <div hlmEmptyTitle>Failed to load this place</div>
              <div hlmEmptyDescription>There was an error loading its contents. Please try again.</div>
            </div>
          </div>
        } @else if (!contents()) {
          <div class="flex justify-center py-16">
            <hlm-spinner />
          </div>
        } @else if (contents(); as c) {
          <div class="flex flex-wrap items-center justify-between gap-2">
            <div class="min-w-0">
              <h2 class="flex items-center gap-2 text-lg font-semibold">
                <ng-icon hlm [name]="placeIcon()" size="sm" class="shrink-0 text-muted-foreground" />
                <span class="truncate">{{ placeName() }}</span>
              </h2>
              <p class="text-sm text-muted-foreground tabular-nums">
                {{ c.directItemCount }} here@if (c.nestedItemCount > 0) {<span> · {{ c.nestedItemCount }} more inside containers</span>}
              </p>
            </div>
            <div class="flex flex-wrap items-center gap-2">
              @if (c.containerItem; as containerItem) {
                <button hlmBtn variant="ghost" size="sm" (click)="containerDetails.emit(containerItem)">Details</button>
              }
              <a hlmBtn variant="outline" size="sm" routerLink="/inventory/items" [queryParams]="openAsListParams()">Open as list</a>
              <app-create-entry-dialog
                triggerSize="sm"
                [initialLocation]="addLocation()"
                [initialLocationLabel]="placeName()"
                (created)="changed.emit()"
              >
                <ng-icon hlm name="lucidePlus" size="sm" />
                Add item here
              </app-create-entry-dialog>
            </div>
          </div>

          @if (c.containers.length === 0 && c.items.length === 0 && c.supplies.length === 0) {
            <div hlmEmpty>
              <div hlmEmptyHeader>
                <div hlmEmptyMedia variant="icon">
                  <ng-icon name="lucidePackage" />
                </div>
                <div hlmEmptyTitle>Nothing here yet</div>
                <div hlmEmptyDescription>This place has no items or supplies in it.</div>
              </div>
              <div hlmEmptyContent>
                <app-create-entry-dialog
                  [initialLocation]="addLocation()"
                  [initialLocationLabel]="placeName()"
                  (created)="changed.emit()"
                >
                  <ng-icon hlm name="lucidePlus" size="sm" />
                  Add item here
                </app-create-entry-dialog>
              </div>
            </div>
          } @else {
            @if (c.containers.length > 0) {
              <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
                @for (container of c.containers; track container.id) {
                  <app-place-card
                    [name]="container.name"
                    icon="lucideBox"
                    [count]="container.itemCount"
                    [lowStockCount]="container.lowStockCount"
                    [previewNames]="container.previewNames"
                    [queryParams]="{ place: 'item:' + container.id }"
                    [chevron]="true"
                  />
                }
              </div>
            }

            @if (c.items.length > 0 || c.supplies.length > 0) {
              <ul class="divide-y divide-border rounded-lg border border-border bg-card text-card-foreground">
                @for (item of c.items; track item.id) {
                  <li>
                    <button
                      type="button"
                      class="flex w-full items-center justify-between gap-2 px-4 py-2.5 text-left text-sm transition-colors hover:bg-accent"
                      (click)="openItem.emit(item)"
                    >
                      <span class="truncate">{{ item.name }}</span>
                      <app-item-status-badge [status]="item.status" />
                    </button>
                  </li>
                }
                @for (row of c.supplies; track row.supply.id) {
                  <li>
                    <button
                      type="button"
                      class="flex w-full items-center justify-between gap-2 px-4 py-2.5 text-left text-sm transition-colors hover:bg-accent"
                      (click)="openSupply.emit(row.supply)"
                    >
                      <span class="flex min-w-0 items-center gap-2">
                        <span class="truncate">{{ row.supply.name }}</span>
                        @if (row.supply.isLow) {
                          <span class="flex shrink-0 items-center gap-1 rounded-full bg-destructive/10 px-2 py-0.5 text-xs font-medium text-destructive">
                            <ng-icon hlm name="lucideTriangleAlert" size="xs" />
                            Low
                          </span>
                        }
                      </span>
                      <span class="shrink-0 text-xs text-muted-foreground tabular-nums">
                        {{ formatQty(row.localQuantity) }} {{ row.supply.unit }}@if (row.localQuantity !== row.supply.totalOnHand) {<span> · of {{ formatQty(row.supply.totalOnHand) }} total</span>}
                      </span>
                    </button>
                  </li>
                }
              </ul>
            }
          }
        }
      </section>
    } @else {
      <section>
        <h2 class="mb-3 text-sm font-medium text-muted-foreground">Browse by place</h2>
        @if (overview().areas.length === 0 && overview().unplacedCount === 0) {
          <p class="text-sm text-muted-foreground">
            No areas yet — set up rooms and places on the
            <a routerLink="/inventory/areas" class="underline underline-offset-4 hover:text-foreground">Areas page</a>.
          </p>
        } @else {
          <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
            @for (area of overview().areas; track area.id) {
              <app-place-card
                [name]="area.name"
                icon="lucideMapPin"
                [count]="area.itemCount"
                [lowStockCount]="area.lowStockCount"
                [queryParams]="{ place: 'area:' + area.id }"
              />
            }
            @if (overview().unplacedCount > 0) {
              <app-place-card
                name="Unplaced"
                icon="lucideMapPinOff"
                [count]="overview().unplacedCount"
                [queryParams]="{ place: 'unplaced' }"
              />
            }
          </div>
        }
      </section>
    }
  `,
})
export class PlaceBrowserComponent {
  overview = input.required<GetInventoryOverviewResponse>();
  /** Drill-down target; null renders the root grid. */
  place = input.required<PlaceRef>();
  /**
   * Direct contents of the drilled place; null while at root, while a *different* place loads
   * (the page blanks it on place change so the spinner shows), or on error. A same-place refresh
   * keeps the prior value so an in-place stock edit doesn't flash the spinner.
   */
  contents = input<PlaceContentsResponse | null>(null);
  error = input(false);

  openItem = output<ItemResponse>();
  openSupply = output<SupplyResponse>();
  /** The drilled container's own quick panel (header "Details" button). */
  containerDetails = output<ItemResponse>();
  /** Something was created/mutated — the page refetches overview + contents. */
  changed = output<void>();

  protected readonly formatQty = formatQty;

  protected breadcrumb = computed<LocationSegmentResponse[]>(() => this.contents()?.path ?? []);

  protected placeName = computed(() => {
    const ref = this.place();
    if (!ref) return '';
    if (ref.kind === 'unplaced') return 'Unplaced';
    const path = this.breadcrumb();
    return path.length > 0 ? path[path.length - 1].name : (this.contents()?.containerItem?.name ?? '');
  });

  protected placeIcon = computed(() => {
    const ref = this.place();
    if (ref?.kind === 'container') return 'lucideBox';
    if (ref?.kind === 'unplaced') return 'lucideMapPinOff';
    return 'lucideMapPin';
  });

  /** Pre-filled location for "Add item here" — the drilled place itself. */
  protected addLocation = computed<ItemLocationValue>(() => {
    const ref = this.place();
    if (ref?.kind === 'area') return { kind: 'area', id: ref.id };
    if (ref?.kind === 'container') return { kind: 'container', id: ref.id };
    return { kind: 'unplaced' };
  });

  /** Escape hatch into the full items list, scoped to this place. */
  protected openAsListParams = computed<Record<string, string>>(() => {
    const ref = this.place();
    if (ref?.kind === 'area') return { area: ref.id } as Record<string, string>;
    if (ref?.kind === 'container') return { container: ref.id } as Record<string, string>;
    return { area: 'unplaced', kind: 'items' };
  });

  /** Breadcrumb targets come from the segment's KIND, never its position in the path. */
  protected segmentParams(seg: LocationSegmentResponse): Record<string, string | null> {
    return {
      place: formatPlaceParam(seg.kind === 'Area'
        ? { kind: 'area', id: seg.id }
        : { kind: 'container', id: seg.id }),
    };
  }
}
