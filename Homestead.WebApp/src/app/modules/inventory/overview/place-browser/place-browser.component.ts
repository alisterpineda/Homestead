import { Component, ChangeDetectionStrategy, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideMapPin,
  lucideMapPinOff,
  lucideBox,
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
import { placeRefToLink, placeDisplayName, type PlaceRef } from './place-param';
import { ItemStatusBadgeComponent } from '../../shared/item-status-badge.component';
import { LOW_STOCK_BADGE_CLASS } from '../../shared/item-status-meta';
import { CreateEntryDialogComponent } from '../../list/create-entry-dialog/create-entry-dialog.component';
import type { ItemLocationValue } from '../../shared/location-picker/location-picker.component';
import { formatQty } from '../../consumables/consumable-shared';
import { UnitPipe } from '../../consumables/unit.pipe';
import type { GetInventoryOverviewResponse } from '@/api/generated/models/get-inventory-overview-response';
import type { PlaceContentsResponse } from '@/api/generated/models/place-contents-response';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';

/**
 * The browse-by-place surface: at root, a card grid of areas (+ Unplaced); drilled in
 * (`place` set), drillable child-container cards and an inline list of the place's direct
 * durables/consumables. Presentational — the page owns the route state and the data fetches; the
 * ancestor breadcrumb lives in the page header, and row clicks bubble up so the page can open
 * its quick panels in place.
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
    UnitPipe,
  ],
  providers: [provideIcons({
    lucideMapPin,
    lucideMapPinOff,
    lucideBox,
    lucideTriangleAlert,
    lucideCircleAlert,
    lucidePackage,
    lucidePlus,
  })],
  template: `
    @if (place(); as ref) {
      <section class="flex flex-col gap-4">
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

          @if (c.childAreas.length === 0 && c.containers.length === 0 && c.items.length === 0 && c.consumables.length === 0) {
            <div hlmEmpty>
              <div hlmEmptyHeader>
                <div hlmEmptyMedia variant="icon">
                  <ng-icon name="lucidePackage" />
                </div>
                <div hlmEmptyTitle>Nothing here yet</div>
                <div hlmEmptyDescription>This place has no sub-areas, durables, or consumables in it.</div>
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
            @if (c.childAreas.length > 0) {
              <div class="space-y-2">
                <h3 class="text-sm font-medium text-muted-foreground">Areas</h3>
                <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
                  @for (childArea of c.childAreas; track childArea.id) {
                    <app-place-card
                      [name]="childArea.name"
                      icon="lucideMapPin"
                      [count]="childArea.itemCount"
                      [lowStockCount]="childArea.lowStockCount"
                      [link]="placeRefToLink({ kind: 'area', id: childArea.id })"
                      [chevron]="true"
                    />
                  }
                </div>
              </div>
            }

            @if (c.containers.length > 0) {
              <div class="space-y-2">
                <h3 class="text-sm font-medium text-muted-foreground">Containers</h3>
                <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
                  @for (container of c.containers; track container.id) {
                    <app-place-card
                      [name]="container.name"
                      icon="lucideBox"
                      [count]="container.itemCount"
                      [lowStockCount]="container.lowStockCount"
                      [previewNames]="container.previewNames"
                      [link]="placeRefToLink({ kind: 'container', id: container.id })"
                      [chevron]="true"
                    />
                  }
                </div>
              </div>
            }

            @if (c.items.length > 0 || c.consumables.length > 0) {
              <div class="space-y-2">
                <h3 class="text-sm font-medium text-muted-foreground">{{ contentsLabel() }}</h3>
                <ul class="divide-y divide-border rounded-lg border border-border bg-card text-card-foreground">
                  @for (item of c.items; track item.id) {
                    <li>
                      <button
                        type="button"
                        class="flex w-full items-center justify-between gap-2 px-4 py-2.5 text-left text-sm transition-colors hover:bg-accent"
                        (click)="openDurable.emit(item)"
                      >
                        <span class="truncate">{{ item.name }}</span>
                        <app-item-status-badge [status]="item.status" />
                      </button>
                    </li>
                  }
                  @for (row of c.consumables; track row.consumable.id) {
                    <li>
                      <button
                        type="button"
                        class="flex w-full items-center justify-between gap-2 px-4 py-2.5 text-left text-sm transition-colors hover:bg-accent"
                        (click)="openConsumable.emit(row.consumable)"
                      >
                        <span class="flex min-w-0 items-center gap-2">
                          <span class="truncate">{{ row.consumable.name }}</span>
                          @if (row.consumable.isLow) {
                            <span [class]="lowStockBadgeClass">
                              <ng-icon hlm name="lucideTriangleAlert" size="xs" />
                              Low
                            </span>
                          }
                        </span>
                        <span class="shrink-0 text-xs text-muted-foreground tabular-nums">
                          {{ formatQty(row.localQuantity) }} {{ row.consumable.unit | unit }}@if (row.localQuantity !== row.consumable.totalOnHand) {<span> · of {{ formatQty(row.consumable.totalOnHand) }} total</span>}
                        </span>
                      </button>
                    </li>
                  }
                </ul>
              </div>
            }
          }
        }
      </section>
    } @else if (overview(); as ov) {
      <section>
        <h2 class="mb-3 text-sm font-medium text-muted-foreground">Browse by place</h2>
        @if (rootAreas().length === 0 && ov.unplacedCount === 0) {
          <p class="text-sm text-muted-foreground">
            No areas yet — set up rooms and places on the
            <a routerLink="/inventory/areas" class="underline underline-offset-4 hover:text-foreground">Areas page</a>.
          </p>
        } @else {
          <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
            @for (area of rootAreas(); track area.id) {
              <app-place-card
                [name]="area.name"
                icon="lucideMapPin"
                [count]="area.itemCount"
                [lowStockCount]="area.lowStockCount"
                [link]="placeRefToLink({ kind: 'area', id: area.id })"
              />
            }
            @if (ov.unplacedCount > 0) {
              <app-place-card
                name="Unplaced"
                icon="lucideMapPinOff"
                [count]="ov.unplacedCount"
                [link]="placeRefToLink({ kind: 'unplaced' })"
              />
            }
          </div>
        }
      </section>
    }
  `,
})
export class PlaceBrowserComponent {
  /** The area grid's source; null while the root overview is still loading (drilled views ignore it). */
  overview = input<GetInventoryOverviewResponse | null>(null);
  /** Drill-down target; null renders the root grid. */
  place = input.required<PlaceRef>();
  /**
   * Direct contents of the drilled place; null while at root, while a *different* place loads
   * (the page blanks it on place change so the spinner shows), or on error. A same-place refresh
   * keeps the prior value so an in-place stock edit doesn't flash the spinner.
   */
  contents = input<PlaceContentsResponse | null>(null);
  error = input(false);

  openDurable = output<DurableResponse>();
  openConsumable = output<ConsumableResponse>();
  /** The drilled container's own quick panel (header "Details" button). */
  containerDetails = output<DurableResponse>();
  /** Something was created/mutated — the page refetches overview + contents. */
  changed = output<void>();

  protected readonly formatQty = formatQty;
  protected readonly placeRefToLink = placeRefToLink;
  protected readonly lowStockBadgeClass = LOW_STOCK_BADGE_CLASS;

  /** Top-level areas only; nested areas surface by drilling into their parent. */
  protected rootAreas = computed(() =>
    (this.overview()?.areas ?? []).filter(a => a.parentAreaId == null));

  protected placeName = computed(() => placeDisplayName(this.place(), this.contents()));

  /** Heading for the loose-contents list — names exactly what it holds (durables, consumables, or both). */
  protected contentsLabel = computed(() => {
    const c = this.contents();
    const hasItems = (c?.items.length ?? 0) > 0;
    const hasConsumables = (c?.consumables.length ?? 0) > 0;
    if (hasItems && hasConsumables) return 'Durables & consumables';
    return hasConsumables ? 'Consumables' : 'Durables';
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
    return { area: 'unplaced', kind: 'durables' };
  });
}
