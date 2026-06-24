import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  viewChild,
  TemplateRef,
  afterNextRender,
  OnDestroy,
} from '@angular/core';
import { ActivatedRoute, Router, NavigationEnd } from '@angular/router';
import { NgTemplateOutlet } from '@angular/common';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, of, catchError, filter } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmBreadcrumbImports } from '@/shared/components/spartan/breadcrumb';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { DurableDetailPageComponent } from '../../durables/durable-detail-page/durable-detail-page.component';
import { ConsumableDetailPageComponent } from '../../consumables/consumable-detail-page.component';
import type { ItemDetailResponse } from '@/api/generated/models/item-detail-response';

/**
 * Thin dispatcher for `/inventory/items/:id`. A durable and a consumable are the same `Item` row
 * underneath, discriminated by {@link ItemDetailResponse.fungibility}; this loads the unified detail in
 * one round-trip, owns the loading / not-found states centrally, then renders the kind-specific child
 * — preloaded with the matching payload so the child renders without a second fetch. The children
 * share this component's `ActivatedRoute`, so they still read the same `:id` for their secondary loads.
 *
 * The shared `Items › name` breadcrumb lives here, not in the children: the dispatcher persists across
 * `:id` navigations (its `item()` drops to null and the children remount), so hosting the crumb here
 * keeps it flicker-free regardless of which kind renders below.
 */
@Component({
  selector: 'app-item-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    NgIcon,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmBreadcrumbImports,
    DurableDetailPageComponent,
    ConsumableDetailPageComponent,
  ],
  providers: [provideIcons({ lucideCircleAlert })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <ng-template #headerTitle>
      <nav hlmBreadcrumb class="min-w-0">
        <ol hlmBreadcrumbList class="flex-nowrap overflow-hidden">
          <li hlmBreadcrumbItem>
            <a hlmBreadcrumbLink link="/inventory/items">Items</a>
          </li>
          <li hlmBreadcrumbSeparator></li>
          <li hlmBreadcrumbItem class="min-w-0">
            <span hlmBreadcrumbPage
                  [title]="name()"
                  class="truncate max-w-[150px] sm:max-w-[250px] md:max-w-[350px]">
              {{ name() }}
            </span>
          </li>
        </ol>
      </nav>
    </ng-template>

    <ng-template #notFoundState>
      <div class="flex min-h-0 flex-1 flex-col gap-6 p-4 md:p-6">
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Item not found</div>
            <div hlmEmptyDescription>It may have been deleted.</div>
          </div>
        </div>
      </div>
    </ng-template>

    @if (item(); as it) {
      @if (it.fungibility === 'NonFungible' && it.durable) {
        <app-durable-detail-page [preloaded]="it.durable" />
      } @else if (it.fungibility === 'Fungible' && it.consumable) {
        <app-consumable-detail-page [preloaded]="it.consumable" />
      } @else {
        <!-- Defensive: the item resolved but its kind/payload is unrenderable (contract drift) —
             show feedback rather than a blank surface. -->
        <ng-container [ngTemplateOutlet]="notFoundState" />
      }
    } @else if (notFound()) {
      <ng-container [ngTemplateOutlet]="notFoundState" />
    } @else {
      <div class="flex justify-center py-16"><hlm-spinner /></div>
    }
  `,
})
export class ItemDetailPageComponent implements OnDestroy {
  private itemApi = inject(InventoryItemApiService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private pageHeaderService = inject(PageHeaderService);

  private headerTitleTpl = viewChild.required<TemplateRef<unknown>>('headerTitle');

  constructor() {
    // The shell clears the registered title on every NavigationStart, so re-register after each
    // navigation — drilling :id → :id reuses this dispatcher and would otherwise lose the crumb.
    afterNextRender(() => {
      this.pageHeaderService.setTitle(this.headerTitleTpl());
    });
    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      takeUntilDestroyed(),
    ).subscribe(() => {
      this.pageHeaderService.setTitle(this.headerTitleTpl());
    });
  }

  ngOnDestroy(): void {
    this.pageHeaderService.clearTitle();
  }

  private itemId = toSignal(this.route.paramMap.pipe(map((p) => p.get('id'))), { initialValue: null });

  // The breadcrumb leaf — resolved from whichever kind loaded, falling back to a generic label while
  // the item is in flight or unrenderable.
  protected name = computed(() => {
    const it = this.item();
    return it?.durable?.name ?? it?.consumable?.consumable?.name ?? 'Item';
  });

  // The spinner shows whenever `item()` is null and we're not in the not-found state, so a separate
  // loading flag would be write-only — `notFound` is the only error state the template reads.
  notFound = signal(false);

  private fetchedItem = toSignal<ItemDetailResponse | null>(
    toObservable(this.itemId).pipe(
      switchMap((id) => {
        if (!id) return of(null);
        this.notFound.set(false);
        return this.itemApi.getItem({ id }).pipe(
          catchError(() => {
            this.notFound.set(true);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  // Only surface the item once it matches the current route id. A navigation therefore drops to the
  // central spinner (the previous item no longer matches) and remounts the child fresh with the
  // correct preload — never briefly rendering the prior item or the wrong kind's child.
  protected item = computed<ItemDetailResponse | null>(() => {
    const it = this.fetchedItem();
    return it && it.id === this.itemId() ? it : null;
  });
}
