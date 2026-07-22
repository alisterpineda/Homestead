import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  signal,
  computed,
  untracked,
  DestroyRef,
  LOCALE_ID,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, of, catchError } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePencil,
  lucideCircleAlert,
  lucideBox,
  lucideBoxes,
  lucideCpu,
  lucidePlus,
  lucideMapPin,
  lucideChevronLeft,
  lucideChevronRight,
  lucideEllipsisVertical,
  lucideArchive,
  lucideArchiveRestore,
  lucideTrash2,
  lucideHistory,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import { InventoryPlaceApiService } from '@/api/services/inventory-place-api.service';
import { ItemStatusBadgeComponent } from '../../shared/item-status-badge.component';
import { TagBadgeComponent } from '@/shared/components/tag-badge/tag-badge.component';
import { isTerminalStatus } from '../../shared/item-status-meta';
import { EditDurableSheetComponent } from '../edit-durable-sheet/edit-durable-sheet.component';
import { DurableQuickPanelComponent } from '../durable-quick-panel/durable-quick-panel.component';
import { CreateEntryDialogComponent } from '../../list/create-entry-dialog/create-entry-dialog.component';
import type { ItemLocationValue } from '../../shared/location-picker/location-picker.component';
import { formatPath } from '../../list/inventory-row';
import { acquisitionSummary } from '../acquisition-summary';
import { formatQty } from '../../consumables/consumable-shared';
import { UnitPipe } from '../../consumables/unit.pipe';
import { formatRelativeAge } from '@/shared/utils/relative-age';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { replace } from '@/shared/json-patch';
import { handleMutationError, handleDetailLoadError } from '@/shared/utils/rxjs-error.utils';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { PlaceContentsResponse } from '@/api/generated/models/place-contents-response';
import type { GetDurableActivityResponse } from '@/api/generated/models/get-durable-activity-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

/**
 * The canonical surface for a non-fungible durable, rendered by the `/inventory/items/:id` dispatcher:
 * full record (value, serial, status, location, notes) with full editing via the shared edit sheet,
 * and — when the durable is a container — its direct contents, or — when an assembly — its
 * components. Durable↔consumable parity with
 * {@link ConsumableDetailPageComponent}; containers are drilled *into* here rather than acted *on*, so
 * every container place-card and breadcrumb segment routes to this page (see `placeRefToLink`).
 */
@Component({
  selector: 'app-durable-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmEmptyImports,
    HlmAlertDialogImports,
    HlmDropdownMenuImports,
    ItemStatusBadgeComponent,
    TagBadgeComponent,
    EditDurableSheetComponent,
    DurableQuickPanelComponent,
    CreateEntryDialogComponent,
    UnitPipe,
  ],
  providers: [provideIcons({
    lucidePencil,
    lucideCircleAlert,
    lucideBox,
    lucideBoxes,
    lucideCpu,
    lucidePlus,
    lucideMapPin,
    lucideChevronLeft,
    lucideChevronRight,
    lucideEllipsisVertical,
    lucideArchive,
    lucideArchiveRestore,
    lucideTrash2,
    lucideHistory,
  })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-6 p-4 md:p-6">
      @if (durable(); as it) {
        @if (loadError()) {
          <div class="flex items-center justify-between gap-3 rounded-md border border-dashed border-destructive/50 p-3 text-sm text-destructive">
            <span>Couldn't refresh. Showing the last loaded version.</span>
            <button type="button" hlmBtn variant="outline" size="sm" (click)="onChanged()">Retry</button>
          </div>
        }
        <!-- Header -->
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div class="space-y-1">
            <div class="flex flex-wrap items-center gap-3">
              @if (it.isContainer) {
                <ng-icon hlm name="lucideBox" size="sm" class="shrink-0 text-muted-foreground" aria-label="Container" />
              } @else if (it.hasComponents) {
                <ng-icon hlm name="lucideCpu" size="sm" class="shrink-0 text-muted-foreground" aria-label="Assembly" />
              }
              <h1 class="text-2xl font-semibold">{{ it.name }}</h1>
              <app-item-status-badge [status]="it.status" />
            </div>
            <!-- Headline answer: where the durable lives. The consumable's parallel is its on-hand
                 metric; a place name isn't a single scannable number, so it's sized down deliberately. -->
            <p class="flex items-center gap-1.5 text-lg font-medium"
               [class.text-muted-foreground]="locationText() === 'Unplaced'">
              <ng-icon hlm name="lucideMapPin" size="sm" class="shrink-0 text-muted-foreground" />
              <span class="break-words">{{ locationText() }}</span>
              @if (it.compartment) {
                <span class="text-muted-foreground">&middot; {{ it.compartment }}</span>
              }
            </p>
            <p class="text-xs text-muted-foreground">Updated {{ updatedRelative() }}</p>
            @if (it.notes) {
              <p class="max-w-prose whitespace-pre-wrap text-sm text-muted-foreground">{{ it.notes }}</p>
            }
            @if (it.tags?.length) {
              <div class="flex flex-wrap gap-1 pt-1">
                @for (t of it.tags ?? []; track t.id) {
                  <a [routerLink]="['/inventory/items']" [queryParams]="{ tagIds: t.id }">
                    <app-tag-badge [path]="t.path" [color]="t.effectiveColor" [compact]="true" />
                  </a>
                }
              </div>
            }
          </div>
          <div class="flex items-center gap-2">
            <button hlmBtn variant="outline" size="sm" (click)="openEdit(it)">
              <ng-icon hlm name="lucidePencil" size="sm" />
              Edit
            </button>
            <button hlmBtn variant="outline" size="icon-sm" [hlmDropdownMenuTrigger]="actionsMenu" aria-label="Durable actions">
              <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
            </button>
            <ng-template #actionsMenu>
              <hlm-dropdown-menu class="w-44">
                <hlm-dropdown-menu-group>
                  <button hlmDropdownMenuItem (triggered)="onArchiveRestore(it)" [disabled]="archiving()">
                    <ng-icon hlm [name]="terminal() ? 'lucideArchiveRestore' : 'lucideArchive'" size="sm" />
                    {{ terminal() ? 'Restore' : 'Archive' }}
                  </button>
                  <hlm-dropdown-menu-separator />
                  <button hlmDropdownMenuItem variant="destructive" (triggered)="onDeleteRequest()">
                    <ng-icon hlm name="lucideTrash2" size="sm" />
                    Delete
                  </button>
                </hlm-dropdown-menu-group>
              </hlm-dropdown-menu>
            </ng-template>
          </div>
        </div>

        <!-- Details — hidden entirely when the durable carries no value/serial/acquired/lent fields
             (the common case in current data); location now leads as the headline above. -->
        @if (hasDetails()) {
          <section class="space-y-2">
            <h2 class="text-sm font-medium text-muted-foreground">Details</h2>
            <dl class="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 rounded-md border p-4 text-sm sm:max-w-lg">
              @if (it.value != null) {
                <dt class="text-muted-foreground">Est. value</dt>
                <dd class="tabular-nums">{{ it.value }}{{ it.valueCurrencySymbol ? ' ' + it.valueCurrencySymbol : '' }}</dd>
              }
              @if (it.serial) {
                <dt class="text-muted-foreground">Serial</dt>
                <dd class="break-words">{{ it.serial }}</dd>
              }
              @if (it.model) {
                <dt class="text-muted-foreground">Model</dt>
                <dd class="break-words">{{ it.model }}</dd>
              }
              @if (it.brandId) {
                <dt class="text-muted-foreground">Brand</dt>
                <dd class="break-words">
                  <a
                    routerLink="/inventory/items"
                    [queryParams]="{ brand: it.brandId }"
                    class="hover:underline"
                  >{{ it.brandName }}</a>
                </dd>
              }
              @if (acquisitionLine(); as acquired) {
                <dt class="text-muted-foreground">Acquired</dt>
                <dd class="break-words tabular-nums">{{ acquired }}</dd>
              }
              @if (it.status === 'Lent' && it.lentTo) {
                <dt class="text-muted-foreground">Lent to</dt>
                <dd class="break-words">{{ it.lentTo }}</dd>
              }
            </dl>
          </section>
        }

        <!-- Components (a non-container: a regular item or, once it has parts, an assembly) -->
        @if (!it.isContainer) {
          <section class="space-y-2">
            <div class="flex items-center justify-between gap-2">
              <h2 class="text-sm font-medium text-muted-foreground">Components</h2>
              <div class="flex items-center gap-2">
                <!-- Intent action: convert an empty regular item into a browsable container. Hidden once
                     it has parts (an assembly can't be flipped to a container — the backend guards it too). -->
                @if (!it.hasComponents) {
                  <button hlmBtn variant="outline" size="sm" (click)="onStoreItemsInside(it)" [disabled]="storingItems()">
                    <ng-icon hlm name="lucideBox" size="sm" />
                    Store items inside
                  </button>
                }
                <app-create-entry-dialog
                  triggerSize="sm"
                  [initialLocation]="addComponentLocation()"
                  [initialLocationLabel]="it.name"
                  (created)="onChanged()"
                >
                  <ng-icon hlm name="lucidePlus" size="sm" />
                  Add part
                </app-create-entry-dialog>
              </div>
            </div>
            @if (!it.hasComponents) {
              <!-- Empty regular item: no components yet, so nothing is fetched. Use "Add part" above. -->
              <div class="rounded-md border border-dashed p-6 text-center text-sm text-muted-foreground">
                No components yet.
              </div>
            } @else if (contentsError()) {
              <div class="rounded-md border border-dashed p-6 text-center text-sm text-muted-foreground">
                Couldn't load components.
                <button type="button" class="underline underline-offset-4 hover:text-foreground" (click)="onChanged()">Retry</button>
              </div>
            } @else if (!contents()) {
              <div class="flex justify-center py-8"><hlm-spinner /></div>
            } @else if (contentItems().length === 0) {
              <div class="rounded-md border border-dashed p-6 text-center text-sm text-muted-foreground">
                No components yet.
              </div>
            } @else {
              <ul class="divide-y divide-border rounded-lg border border-border bg-card text-card-foreground">
                @for (child of contentItems(); track child.id) {
                  <li>
                    <button
                      type="button"
                      class="flex w-full items-center justify-between gap-2 px-4 py-2.5 text-left text-sm transition-colors hover:bg-accent"
                      (click)="onOpenDurable(child)"
                    >
                      <span class="flex min-w-0 items-center gap-2">
                        <span class="truncate">{{ child.name }}</span>
                        @if (child.compartment) {
                          <span class="shrink-0 rounded-sm bg-muted px-1.5 py-0.5 text-xs text-muted-foreground">{{ child.compartment }}</span>
                        }
                      </span>
                      <app-item-status-badge [status]="child.status" />
                    </button>
                  </li>
                }
              </ul>
            }
          </section>
        }

        <!-- Contents (containers only) -->
        @if (it.isContainer) {
          <section class="space-y-2">
            <div class="flex items-center justify-between gap-2">
              <h2 class="text-sm font-medium text-muted-foreground">Contents</h2>
              <app-create-entry-dialog
                triggerSize="sm"
                [initialLocation]="addComponentLocation()"
                [initialLocationLabel]="it.name"
                (created)="onChanged()"
              >
                <ng-icon hlm name="lucidePlus" size="sm" />
                Add item
              </app-create-entry-dialog>
            </div>
            @if (contentsError()) {
              <div class="rounded-md border border-dashed p-6 text-center text-sm text-muted-foreground">
                Couldn't load contents.
                <button type="button" class="underline underline-offset-4 hover:text-foreground" (click)="onChanged()">Retry</button>
              </div>
            } @else if (!contents()) {
              <div class="flex justify-center py-8"><hlm-spinner /></div>
            } @else if (isEmptyContents()) {
              <div class="rounded-md border border-dashed p-6 text-center text-sm text-muted-foreground">
                This container is empty.
              </div>
            } @else {
              <ul class="divide-y divide-border rounded-lg border border-border bg-card text-card-foreground">
                @for (container of contentContainers(); track container.id) {
                  <li>
                    <a
                      class="flex w-full items-center justify-between gap-2 px-4 py-2.5 text-left text-sm transition-colors hover:bg-accent"
                      [routerLink]="['/inventory/items', container.id]"
                    >
                      <span class="flex min-w-0 items-center gap-2">
                        <ng-icon hlm name="lucideBox" size="sm" class="shrink-0 text-muted-foreground" />
                        <span class="truncate">{{ container.name }}</span>
                      </span>
                      <span class="flex shrink-0 items-center gap-2 text-xs text-muted-foreground tabular-nums">
                        {{ container.itemCount }} {{ container.itemCount === 1 ? 'item' : 'items' }}
                        <ng-icon hlm name="lucideChevronRight" size="sm" />
                      </span>
                    </a>
                  </li>
                }
                @for (child of contentItems(); track child.id) {
                  <li>
                    <button
                      type="button"
                      class="flex w-full items-center justify-between gap-2 px-4 py-2.5 text-left text-sm transition-colors hover:bg-accent"
                      (click)="onOpenDurable(child)"
                    >
                      <span class="truncate">{{ child.name }}</span>
                      <app-item-status-badge [status]="child.status" />
                    </button>
                  </li>
                }
                @for (row of contentConsumables(); track row.consumable.id) {
                  <li>
                    <a
                      class="flex w-full items-center justify-between gap-2 px-4 py-2.5 text-left text-sm transition-colors hover:bg-accent"
                      [routerLink]="['/inventory/items', row.consumable.id]"
                    >
                      <span class="flex min-w-0 items-center gap-2">
                        <ng-icon hlm name="lucideBoxes" size="sm" class="shrink-0 text-muted-foreground" />
                        <span class="truncate">{{ row.consumable.name }}</span>
                      </span>
                      <span class="shrink-0 text-xs text-muted-foreground tabular-nums">
                        {{ formatQty(row.localQuantity) }} {{ row.consumable.unit | unit }}
                      </span>
                    </a>
                  </li>
                }
              </ul>
            }
          </section>
        }

        <!-- History — a human-readable metadata activity feed (the actor is embedded in each summary). -->
        <section class="space-y-2">
          <h2 class="text-sm font-medium text-muted-foreground">History</h2>
          @if (activityError()) {
            <div class="rounded-md border border-dashed p-6 text-center text-sm text-muted-foreground">
              Couldn't load history.
              <button type="button" class="underline underline-offset-4 hover:text-foreground" (click)="onChanged()">Retry</button>
            </div>
          } @else if (!activityLoaded()) {
            <div class="flex justify-center py-8"><hlm-spinner /></div>
          } @else if (activity().length === 0) {
            <div class="rounded-md border border-dashed p-6 text-center text-sm text-muted-foreground">
              No activity recorded yet.
            </div>
          } @else {
            <ul class="divide-y divide-border rounded-md border">
              @for (a of activity(); track a.id) {
                <li class="flex items-center gap-2.5 px-4 py-2">
                  <ng-icon hlm name="lucideHistory" size="xs" class="shrink-0 text-muted-foreground" />
                  <p class="text-xs text-muted-foreground">
                    {{ a.summary }}
                    <span class="mx-0.5">&middot;</span>
                    {{ relativeAge(a.timestamp) }}
                  </p>
                </li>
              }
            </ul>

            @if (activityTotalPages() > 1) {
              <div class="flex items-center justify-end gap-2">
                <span class="text-xs text-muted-foreground">Page {{ page() }} of {{ activityTotalPages() }}</span>
                <button hlmBtn variant="outline" size="icon-sm" [disabled]="page() <= 1" (click)="prevPage()" aria-label="Previous page">
                  <ng-icon hlm name="lucideChevronLeft" size="sm" />
                </button>
                <button hlmBtn variant="outline" size="icon-sm" [disabled]="page() >= activityTotalPages()" (click)="nextPage()" aria-label="Next page">
                  <ng-icon hlm name="lucideChevronRight" size="sm" />
                </button>
              </div>
            }
          }
        </section>
      } @else if (notFound()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Durable not found</div>
            <div hlmEmptyDescription>It may have been deleted.</div>
          </div>
        </div>
      } @else if (loadError()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Couldn't load durable</div>
            <div hlmEmptyDescription>Something went wrong. Check your connection and try again.</div>
          </div>
          <div hlmEmptyContent>
            <button type="button" hlmBtn variant="outline" (click)="onChanged()">Retry</button>
          </div>
        </div>
      } @else if (loading()) {
        <div class="flex justify-center py-16"><hlm-spinner /></div>
      }
    </div>

    <app-durable-quick-panel
      [durable]="quickPanelDurable()"
      [state]="durableQuickPanelState()"
      (changed)="onChanged()"
      (closed)="durableQuickPanelState.set('closed')"
      (viewContents)="onQuickPanelViewContents($event)"
    />

    <app-edit-durable-sheet
      [durable]="editDurable()"
      [state]="editState()"
      (saved)="onEditSaved()"
      (closed)="editState.set('closed')"
    />

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete {{ durable()?.name ?? 'durable' }}?</h2>
          <p hlmAlertDialogDescription>
            This permanently removes the durable. Durables with contents cannot be deleted — archive them instead.
          </p>
        </div>
        @if (deleteError()) {
          <p class="text-sm text-destructive">{{ deleteError() }}</p>
        }
        <div hlmAlertDialogFooter>
          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
          <button
            hlmAlertDialogAction
            variant="destructive"
            (click)="confirmDelete(ctx)"
            [disabled]="deleting()"
          >
            {{ deleting() ? 'Deleting...' : 'Delete' }}
          </button>
        </div>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class DurableDetailPageComponent {
  private durableApi = inject(InventoryDurableApiService);
  private placeApi = inject(InventoryPlaceApiService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  protected readonly formatQty = formatQty;

  // Optional payload from the `/inventory/items/:id` dispatcher, which already fetched the record. When
  // present we render from it and skip the initial GET; absent (direct/standalone use) we fetch by id.
  preloaded = input<DurableResponse | null>(null);

  private durableId = toSignal(this.route.paramMap.pipe(map((p) => p.get('id'))), { initialValue: null });

  refreshTrigger = signal(0);
  loading = signal(false);
  notFound = signal(false);
  // A non-404 load/refetch failure — retryable, and never allowed to replace already-rendered data.
  loadError = signal(false);
  contentsError = signal(false);
  activityError = signal(false);

  private detailKey = computed(() => ({ id: this.durableId(), tick: this.refreshTrigger() }));

  private fetchedDurable = toSignal<DurableResponse | null>(
    toObservable(this.detailKey).pipe(
      switchMap(({ id, tick }) => {
        if (!id) return of(null);
        // Initial render of a dispatcher-preloaded item: reuse the preload, skip the duplicate GET.
        // A refresh (tick > 0) or a missing preload always hits the API.
        if (tick === 0 && untracked(() => this.preloaded())?.id === id) return of(null);
        this.loading.set(true);
        this.notFound.set(false);
        this.loadError.set(false);
        return this.durableApi.getDurable({ id }).pipe(
          map((it) => {
            this.loading.set(false);
            return it;
          }),
          catchError((err) => handleDetailLoadError(err, { notFound: this.notFound, loadError: this.loadError }, this.loading)),
        );
      }),
    ),
    { initialValue: null },
  );

  // Prefer freshly-fetched data once it matches the routed id; otherwise fall back to the preload.
  durable = computed<DurableResponse | null>(() => {
    const id = this.durableId();
    const fetched = this.fetchedDurable();
    if (fetched && fetched.id === id) return fetched;
    const pre = this.preloaded();
    return pre && pre.id === id ? pre : null;
  });

  protected isContainer = computed(() => this.durable()?.isContainer === true);
  // Assembly-ness is derived server-side: a non-container with ≥1 live component child.
  protected hasComponents = computed(() => this.durable()?.hasComponents === true);
  // A container fetches its stored contents; a non-container with components fetches those components.
  // An empty regular item holds nothing, so it renders a static empty state without fetching.
  protected isHolder = computed(() => this.isContainer() || this.hasComponents());
  // "Add item"/"Add part" pre-parents the create dialog under this item; the server derives the
  // relationship (stored-in vs component) from this item's IsContainer.
  protected addComponentLocation = computed<ItemLocationValue | null>(() => {
    const it = this.durable();
    return it ? { kind: 'container', id: it.id } : null;
  });
  protected terminal = computed(() => {
    const it = this.durable();
    return it ? isTerminalStatus(it.status) : false;
  });

  protected locationText = computed(() => {
    const it = this.durable();
    return it ? formatPath(it) : '';
  });

  private locale = inject(LOCALE_ID);

  /** Composed "Acquired" row (kind · date · cost · from …), null when nothing is recorded. */
  protected acquisitionLine = computed<string | null>(() => {
    const it = this.durable();
    return it ? acquisitionSummary(it, this.locale) : null;
  });

  // Drives whether the Details box renders at all — when value/serial/acquired/lent are all empty it
  // would be an empty card, so it's hidden (location now leads as the headline, not a Details row).
  protected hasDetails = computed(() => {
    const it = this.durable();
    return !!it && (it.value != null || !!it.serial || !!it.model || !!it.brandId
      || this.acquisitionLine() !== null
      || (it.status === 'Lent' && !!it.lentTo));
  });

  protected updatedRelative = computed(() => {
    const updatedAt = this.durable()?.updatedAt;
    return updatedAt ? formatRelativeAge(updatedAt) : '';
  });

  // The container's direct contents — one fetch yields the `.path` trail and the `.items`/
  // `.containers`/`.consumables` lists. Skipped (and left null) for a leaf durable. Keyed on the refresh
  // tick so an in-place edit/quick-panel change re-reads the contents.
  private contentsKey = computed(() => ({
    id: this.isHolder() ? this.durableId() : null,
    tick: this.refreshTrigger(),
  }));

  contents = toSignal<PlaceContentsResponse | null>(
    toObservable(this.contentsKey).pipe(
      switchMap(({ id }) => {
        this.contentsError.set(false);
        if (!id) return of(null);
        return this.placeApi.getPlaceContents({ ContainerItemId: id }).pipe(
          catchError(() => {
            this.contentsError.set(true);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  protected contentContainers = computed(() => this.contents()?.containers ?? []);
  protected contentItems = computed(() => this.contents()?.items ?? []);
  protected contentConsumables = computed(() => this.contents()?.consumables ?? []);
  protected isEmptyContents = computed(() =>
    this.contentContainers().length === 0
    && this.contentItems().length === 0
    && this.contentConsumables().length === 0);

  // --- Activity history (every durable, leaf or container) ---
  protected readonly relativeAge = formatRelativeAge;
  // Resets to page 1 when the routed durable changes — Angular reuses this component on a :id-only
  // navigation (a contents-list drill or the quick panel's "View details"), so a plain signal
  // would carry the prior durable's page number into the new durable's activity query.
  page = seededLinkedSignal(this.durableId, () => 1);

  private activityKey = computed(() => ({
    id: this.durableId(),
    page: this.page(),
    tick: this.refreshTrigger(),
  }));

  private activityPage = toSignal(
    toObservable(this.activityKey).pipe(
      switchMap(({ id, page }) => {
        this.activityError.set(false);
        if (!id) return of(null);
        return this.durableApi.getDurableActivity({ id, Page: page, PageSize: 20 }).pipe(
          catchError(() => {
            this.activityError.set(true);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  protected activity = computed<GetDurableActivityResponse[]>(() => this.activityPage()?.items ?? []);
  protected activityTotalPages = computed(() => this.activityPage()?.totalPages ?? 1);
  // Distinguishes the initial in-flight fetch (null) from a resolved-but-empty page, so the empty
  // state can't flash before the first response lands.
  protected activityLoaded = computed(() => this.activityPage() !== null);

  // Hosted leaf-durable quick panel (opened from the contents list). This panel, the edit sheet, and
  // the delete dialog all reset to 'closed' when the routed durable changes: the component is reused on
  // a :id-only navigation, so without the reset an overlay opened for the previous durable would linger
  // over the newly-loaded one (e.g. the leaf panel's own "View details" routes to this component).
  quickPanelDurable = signal<DurableResponse | null>(null);
  durableQuickPanelState = seededLinkedSignal<string | null, BrnDialogState>(this.durableId, () => 'closed');

  // Edit sheet state.
  editDurable = signal<DurableResponse | null>(null);
  editState = seededLinkedSignal<string | null, BrnDialogState>(this.durableId, () => 'closed');

  // Delete dialog state.
  deleteState = seededLinkedSignal<string | null, BrnDialogState>(this.durableId, () => 'closed');
  deleting = signal(false);
  deleteError = signal('');
  archiving = signal(false);
  storingItems = signal(false);

  onChanged(): void {
    this.refreshTrigger.update((v) => v + 1);
  }

  /** Intent action: convert a regular item into a storage container (PATCH /isContainer = true). */
  onStoreItemsInside(durable: DurableResponse): void {
    this.storingItems.set(true);
    this.durableApi.patchDurable({ id: durable.id, body: [replace('/isContainer', true)] }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.storingItems, 'Failed to make this a container.'),
    ).subscribe(() => {
      this.storingItems.set(false);
      toast.success('This item is now a container.');
      this.onChanged();
    });
  }

  prevPage(): void {
    this.page.update((p) => Math.max(1, p - 1));
  }

  nextPage(): void {
    this.page.update((p) => Math.min(this.activityTotalPages(), p + 1));
  }

  openEdit(durable: DurableResponse): void {
    this.editDurable.set(durable);
    this.editState.set('open');
  }

  onEditSaved(): void {
    this.editState.set('closed');
    this.onChanged();
  }

  onOpenDurable(durable: DurableResponse): void {
    this.quickPanelDurable.set(durable);
    this.durableQuickPanelState.set('open');
  }

  /** "View contents" in a hosted leaf panel drills into that container's own detail page. */
  onQuickPanelViewContents(durable: DurableResponse): void {
    this.durableQuickPanelState.set('closed');
    void this.router.navigate(['/inventory/items', durable.id]);
  }

  onArchiveRestore(durable: DurableResponse): void {
    const terminal = isTerminalStatus(durable.status);
    const newStatus = terminal ? 'Active' : 'Archived';
    this.archiving.set(true);
    this.durableApi.patchDurable({ id: durable.id, body: [replace('/status', newStatus)] }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.archiving, 'Failed to update durable.'),
    ).subscribe(() => {
      this.archiving.set(false);
      toast.success(terminal ? 'Durable restored.' : 'Durable archived.');
      this.onChanged();
    });
  }

  onDeleteRequest(): void {
    this.deleteError.set('');
    this.deleteState.set('open');
  }

  confirmDelete(ctx: { close: () => void }): void {
    const it = this.durable();
    if (!it) return;
    this.deleting.set(true);
    this.deleteError.set('');
    this.durableApi.deleteDurable({ id: it.id }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.deleting.set(false);
        ctx.close();
        toast.success('Durable deleted.');
        void this.router.navigate(['/inventory/items']);
      },
      // Inline, not a toast: the open dialog overlay would obscure a toast, and the rejection
      // reason ("has contents — archive instead") is what the user needs to read here.
      error: (err: unknown) => {
        this.deleting.set(false);
        this.deleteError.set(this.errorHandler.getErrorMessage(err, 'Failed to delete durable.'));
      },
    });
  }
}
