import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  DestroyRef,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, of, catchError } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucidePencil,
  lucideCircleAlert,
  lucideBox,
  lucideBoxes,
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
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { InventoryPlaceApiService } from '@/api/services/inventory-place-api.service';
import { ItemStatusBadgeComponent } from '../../shared/item-status-badge.component';
import { isTerminalStatus } from '../../shared/item-status-meta';
import { EditItemSheetComponent } from '../edit-item-sheet/edit-item-sheet.component';
import { ItemQuickPanelComponent } from '../item-quick-panel/item-quick-panel.component';
import { formatPath } from '../../list/inventory-row';
import { formatQty } from '../../supplies/supply-shared';
import { formatRelativeAge } from '@/shared/utils/relative-age';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { replace } from '@/shared/json-patch';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { PlaceContentsResponse } from '@/api/generated/models/place-contents-response';
import type { GetItemActivityResponse } from '@/api/generated/models/get-item-activity-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

/**
 * The canonical surface for a non-fungible item at `/inventory/items/:id`: full record (value,
 * serial, status, location, notes) with full editing via the shared edit sheet, and — when the
 * item is a container — its direct contents. Item↔supply parity with
 * {@link SupplyDetailPageComponent}; containers are drilled *into* here rather than acted *on*, so
 * every container place-card and breadcrumb segment routes to this page (see `placeRefToLink`).
 */
@Component({
  selector: 'app-item-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    DatePipe,
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmEmptyImports,
    HlmAlertDialogImports,
    HlmDropdownMenuImports,
    ItemStatusBadgeComponent,
    EditItemSheetComponent,
    ItemQuickPanelComponent,
  ],
  providers: [provideIcons({
    lucideArrowLeft,
    lucidePencil,
    lucideCircleAlert,
    lucideBox,
    lucideBoxes,
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
      <a routerLink="/inventory/items" class="flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ng-icon hlm name="lucideArrowLeft" size="sm" />
        All items
      </a>

      @if (loading() && !item()) {
        <div class="flex justify-center py-16"><hlm-spinner /></div>
      } @else if (notFound()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Item not found</div>
            <div hlmEmptyDescription>It may have been deleted.</div>
          </div>
        </div>
      } @else if (item(); as it) {
        <!-- Header -->
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div class="space-y-1">
            <div class="flex flex-wrap items-center gap-3">
              @if (it.isContainer) {
                <ng-icon hlm name="lucideBox" size="sm" class="shrink-0 text-muted-foreground" aria-label="Container" />
              }
              <h1 class="text-2xl font-semibold">{{ it.name }}</h1>
              <app-item-status-badge [status]="it.status" />
            </div>
            <p class="text-xs text-muted-foreground">Updated {{ updatedRelative() }}</p>
          </div>
          <div class="flex items-center gap-2">
            <button hlmBtn variant="outline" size="sm" (click)="openEdit(it)">
              <ng-icon hlm name="lucidePencil" size="sm" />
              Edit
            </button>
            <button hlmBtn variant="outline" size="icon-sm" [hlmDropdownMenuTrigger]="actionsMenu" aria-label="Item actions">
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

        <!-- Details -->
        <section class="space-y-2">
          <h2 class="text-sm font-medium text-muted-foreground">Details</h2>
          <dl class="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 rounded-md border p-4 text-sm sm:max-w-lg">
            <dt class="text-muted-foreground">Location</dt>
            <dd class="min-w-0">
              <span class="break-words">{{ locationText() }}</span>
              @if (it.compartment) {
                <span class="text-muted-foreground"> · {{ it.compartment }}</span>
              }
            </dd>
            @if (it.value != null) {
              <dt class="text-muted-foreground">Value</dt>
              <dd class="tabular-nums">{{ it.value }}{{ it.valueCurrencySymbol ? ' ' + it.valueCurrencySymbol : '' }}</dd>
            }
            @if (it.serial) {
              <dt class="text-muted-foreground">Serial</dt>
              <dd class="break-words">{{ it.serial }}</dd>
            }
            @if (it.acquiredOn) {
              <dt class="text-muted-foreground">Acquired on</dt>
              <dd>{{ it.acquiredOn | date: 'mediumDate' }}</dd>
            }
            @if (it.status === 'Lent' && it.lentTo) {
              <dt class="text-muted-foreground">Lent to</dt>
              <dd class="break-words">{{ it.lentTo }}</dd>
            }
          </dl>
          @if (it.notes) {
            <p class="max-w-prose whitespace-pre-wrap text-sm text-muted-foreground">{{ it.notes }}</p>
          }
        </section>

        <!-- Contents (containers only) -->
        @if (it.isContainer) {
          <section class="space-y-2">
            <h2 class="text-sm font-medium text-muted-foreground">Contents</h2>
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
                      (click)="onOpenItem(child)"
                    >
                      <span class="truncate">{{ child.name }}</span>
                      <app-item-status-badge [status]="child.status" />
                    </button>
                  </li>
                }
                @for (row of contentSupplies(); track row.supply.id) {
                  <li>
                    <a
                      class="flex w-full items-center justify-between gap-2 px-4 py-2.5 text-left text-sm transition-colors hover:bg-accent"
                      [routerLink]="['/inventory/supplies', row.supply.id]"
                    >
                      <span class="flex min-w-0 items-center gap-2">
                        <ng-icon hlm name="lucideBoxes" size="sm" class="shrink-0 text-muted-foreground" />
                        <span class="truncate">{{ row.supply.name }}</span>
                      </span>
                      <span class="shrink-0 text-xs text-muted-foreground tabular-nums">
                        {{ formatQty(row.localQuantity) }} {{ row.supply.unit }}
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
      }
    </div>

    <app-item-quick-panel
      [item]="quickPanelItem()"
      [state]="itemQuickPanelState()"
      (changed)="onChanged()"
      (closed)="itemQuickPanelState.set('closed')"
      (viewContents)="onQuickPanelViewContents($event)"
    />

    <app-edit-item-sheet
      [item]="editItem()"
      [state]="editState()"
      (saved)="onEditSaved()"
      (closed)="editState.set('closed')"
    />

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete {{ item()?.name ?? 'item' }}?</h2>
          <p hlmAlertDialogDescription>
            This permanently removes the item. Items with contents cannot be deleted — archive them instead.
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
export class ItemDetailPageComponent {
  private itemApi = inject(InventoryItemApiService);
  private placeApi = inject(InventoryPlaceApiService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);

  protected readonly formatQty = formatQty;

  private itemId = toSignal(this.route.paramMap.pipe(map((p) => p.get('id'))), { initialValue: null });

  refreshTrigger = signal(0);
  loading = signal(false);
  notFound = signal(false);
  contentsError = signal(false);
  activityError = signal(false);

  private detailKey = computed(() => ({ id: this.itemId(), tick: this.refreshTrigger() }));

  item = toSignal<ItemResponse | null>(
    toObservable(this.detailKey).pipe(
      switchMap(({ id }) => {
        if (!id) return of(null);
        this.loading.set(true);
        this.notFound.set(false);
        return this.itemApi.getItem({ id }).pipe(
          map((it) => {
            this.loading.set(false);
            return it;
          }),
          catchError(() => {
            this.loading.set(false);
            this.notFound.set(true);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  protected isContainer = computed(() => this.item()?.isContainer ?? false);
  protected terminal = computed(() => {
    const it = this.item();
    return it ? isTerminalStatus(it.status) : false;
  });

  protected locationText = computed(() => {
    const it = this.item();
    return it ? formatPath(it) : '';
  });

  protected updatedRelative = computed(() => {
    const updatedAt = this.item()?.updatedAt;
    return updatedAt ? formatRelativeAge(updatedAt) : '';
  });

  // The container's direct contents — one fetch yields the `.path` trail and the `.items`/
  // `.containers`/`.supplies` lists. Skipped (and left null) for a leaf item. Keyed on the refresh
  // tick so an in-place edit/quick-panel change re-reads the contents.
  private contentsKey = computed(() => ({
    id: this.isContainer() ? this.itemId() : null,
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
  protected contentSupplies = computed(() => this.contents()?.supplies ?? []);
  protected isEmptyContents = computed(() =>
    this.contentContainers().length === 0
    && this.contentItems().length === 0
    && this.contentSupplies().length === 0);

  // --- Activity history (every item, leaf or container) ---
  protected readonly relativeAge = formatRelativeAge;
  // Resets to page 1 when the routed item changes — Angular reuses this component on a :id-only
  // navigation (a contents-list drill or the quick panel's "Open full view"), so a plain signal
  // would carry the prior item's page number into the new item's activity query.
  page = seededLinkedSignal(this.itemId, () => 1);

  private activityKey = computed(() => ({
    id: this.itemId(),
    page: this.page(),
    tick: this.refreshTrigger(),
  }));

  private activityPage = toSignal(
    toObservable(this.activityKey).pipe(
      switchMap(({ id, page }) => {
        this.activityError.set(false);
        if (!id) return of(null);
        return this.itemApi.getItemActivity({ id, Page: page, PageSize: 20 }).pipe(
          catchError(() => {
            this.activityError.set(true);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  protected activity = computed<GetItemActivityResponse[]>(() => this.activityPage()?.items ?? []);
  protected activityTotalPages = computed(() => this.activityPage()?.totalPages ?? 1);
  // Distinguishes the initial in-flight fetch (null) from a resolved-but-empty page, so the empty
  // state can't flash before the first response lands.
  protected activityLoaded = computed(() => this.activityPage() !== null);

  // Hosted leaf-item quick panel (opened from the contents list). This panel, the edit sheet, and
  // the delete dialog all reset to 'closed' when the routed item changes: the component is reused on
  // a :id-only navigation, so without the reset an overlay opened for the previous item would linger
  // over the newly-loaded one (e.g. the leaf panel's own "Open full view" routes to this component).
  quickPanelItem = signal<ItemResponse | null>(null);
  itemQuickPanelState = seededLinkedSignal<string | null, BrnDialogState>(this.itemId, () => 'closed');

  // Edit sheet state.
  editItem = signal<ItemResponse | null>(null);
  editState = seededLinkedSignal<string | null, BrnDialogState>(this.itemId, () => 'closed');

  // Delete dialog state.
  deleteState = seededLinkedSignal<string | null, BrnDialogState>(this.itemId, () => 'closed');
  deleting = signal(false);
  deleteError = signal('');
  archiving = signal(false);

  onChanged(): void {
    this.refreshTrigger.update((v) => v + 1);
  }

  prevPage(): void {
    this.page.update((p) => Math.max(1, p - 1));
  }

  nextPage(): void {
    this.page.update((p) => Math.min(this.activityTotalPages(), p + 1));
  }

  openEdit(item: ItemResponse): void {
    this.editItem.set(item);
    this.editState.set('open');
  }

  onEditSaved(): void {
    this.editState.set('closed');
    this.onChanged();
  }

  onOpenItem(item: ItemResponse): void {
    this.quickPanelItem.set(item);
    this.itemQuickPanelState.set('open');
  }

  /** "View contents" in a hosted leaf panel drills into that container's own detail page. */
  onQuickPanelViewContents(item: ItemResponse): void {
    this.itemQuickPanelState.set('closed');
    void this.router.navigate(['/inventory/items', item.id]);
  }

  onArchiveRestore(item: ItemResponse): void {
    const terminal = isTerminalStatus(item.status);
    const newStatus = terminal ? 'Active' : 'Archived';
    this.archiving.set(true);
    this.itemApi.patchItem({ id: item.id, body: [replace('/status', newStatus)] }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.archiving, 'Failed to update item.'),
    ).subscribe(() => {
      this.archiving.set(false);
      toast.success(terminal ? 'Item restored.' : 'Item archived.');
      this.onChanged();
    });
  }

  onDeleteRequest(): void {
    this.deleteError.set('');
    this.deleteState.set('open');
  }

  confirmDelete(ctx: { close: () => void }): void {
    const it = this.item();
    if (!it) return;
    this.deleting.set(true);
    this.deleteError.set('');
    this.itemApi.deleteItem({ id: it.id }).subscribe({
      next: () => {
        this.deleting.set(false);
        ctx.close();
        toast.success('Item deleted.');
        void this.router.navigate(['/inventory/items']);
      },
      // Inline, not a toast: the open dialog overlay would obscure a toast, and the rejection
      // reason ("has contents — archive instead") is what the user needs to read here.
      error: (err: unknown) => {
        this.deleting.set(false);
        this.deleteError.set(this.errorHandler.getErrorMessage(err, 'Failed to delete item.'));
      },
    });
  }
}
