import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  viewChild,
  TemplateRef,
  afterNextRender,
  OnDestroy,
} from '@angular/core';
import { ActivatedRoute, RouterLink, Router, NavigationEnd } from '@angular/router';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, filter, combineLatest, catchError } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePencil, lucideCircleAlert } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmBreadcrumbImports } from '@/shared/components/spartan/breadcrumb';
import { HlmBadgeImports } from '@/shared/components/spartan/badge';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { handleDetailLoadError } from '@/shared/utils/rxjs-error.utils';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { CommodityPricesSectionComponent } from './commodity-prices-section.component';
import { EditCommoditySheetComponent } from './edit-commodity-sheet/edit-commodity-sheet.component';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

@Component({
  selector: 'app-commodity-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmEmptyImports,
    HlmBreadcrumbImports,
    HlmBadgeImports,
    CommodityPricesSectionComponent,
    EditCommoditySheetComponent,
  ],
  providers: [provideIcons({ lucidePencil, lucideCircleAlert })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <ng-template #headerTitle>
      <nav hlmBreadcrumb class="min-w-0">
        <ol hlmBreadcrumbList class="flex-nowrap overflow-hidden">
          <li hlmBreadcrumbItem>
            <a hlmBreadcrumbLink link="/finances/commodities">Commodities</a>
          </li>
          <li hlmBreadcrumbSeparator></li>
          <li hlmBreadcrumbItem class="min-w-0">
            <span hlmBreadcrumbPage class="truncate">
              {{ commodity()?.symbol ?? 'Commodity' }}
            </span>
          </li>
        </ol>
      </nav>
    </ng-template>

    <ng-template #headerActions>
      @if (commodity(); as c) {
        @if (!c.isSystemManaged) {
          <button hlmBtn variant="outline" (click)="openEdit()">
            <ng-icon hlm name="lucidePencil" size="sm" />
            Edit
          </button>
        }
      }
    </ng-template>

    <div class="flex min-h-0 flex-1 flex-col gap-4 p-4 md:p-6">
      @if (commodity(); as c) {
        @if (loadError()) {
          <div class="flex items-center justify-between gap-3 rounded-md border border-dashed border-destructive/50 p-3 text-sm text-destructive">
            <span>Couldn't refresh. Showing the last loaded version.</span>
            <button type="button" hlmBtn variant="outline" size="sm" (click)="retry()">Retry</button>
          </div>
        }
        <header class="flex flex-col gap-2 rounded-lg border border-border bg-card p-4 text-card-foreground">
          <div class="flex flex-wrap items-center gap-2">
            <h1 class="text-2xl font-semibold">{{ c.symbol }}</h1>
            @if (c.exchange) {
              <span hlmBadge variant="outline">{{ c.exchange }}</span>
            }
            <span hlmBadge variant="secondary">{{ c.kind }}</span>
            @if (c.isSystemManaged) {
              <span hlmBadge variant="secondary">Read-only</span>
            }
          </div>
          <p class="text-sm text-muted-foreground">{{ c.name }}</p>
        </header>

        <app-commodity-prices-section [commodity]="c" />
      } @else if (loading()) {
        <div class="flex justify-center py-16"><hlm-spinner /></div>
      } @else if (loadError()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Couldn't load commodity</div>
            <div hlmEmptyDescription>Something went wrong. Check your connection and try again.</div>
          </div>
          <div hlmEmptyContent class="flex gap-2">
            <button type="button" hlmBtn variant="outline" (click)="retry()">Retry</button>
            <a hlmBtn variant="ghost" routerLink="/finances/commodities">Back to commodities</a>
          </div>
        </div>
      } @else {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Commodity not found</div>
            <div hlmEmptyDescription>It may have been deleted.</div>
          </div>
          <div hlmEmptyContent>
            <a hlmBtn variant="outline" routerLink="/finances/commodities">Back to commodities</a>
          </div>
        </div>
      }
    </div>

    <app-edit-commodity-sheet
      [commodity]="commodity()"
      [state]="sheetState()"
      (saved)="onSheetSaved()"
      (closed)="onSheetClosed()"
    />
  `,
})
export class CommodityDetailPageComponent implements OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private commodityApi = inject(FinancesCommodityApiService);
  private pageHeaderService = inject(PageHeaderService);

  refreshTrigger = signal(0);
  loading = signal(false);
  // A real 404 → the "not found" empty state; any other error → a retryable loadError that never
  // replaces an already-rendered commodity on a failed refetch.
  notFound = signal(false);
  loadError = signal(false);
  sheetState = signal<BrnDialogState>('closed');

  private headerActionsTpl = viewChild.required<TemplateRef<unknown>>('headerActions');
  private headerTitleTpl = viewChild.required<TemplateRef<unknown>>('headerTitle');

  private id = toSignal(
    this.route.paramMap.pipe(map(p => p.get('id') ?? '')),
    { initialValue: '' },
  );

  commodity = toSignal<CommodityResponse | null, CommodityResponse | null>(
    combineLatest([toObservable(this.id), toObservable(this.refreshTrigger)]).pipe(
      switchMap(([id]) => {
        if (!id) return [null];
        this.loading.set(true);
        this.notFound.set(false);
        this.loadError.set(false);
        return this.commodityApi.getCommodity({ id }).pipe(
          map(c => {
            this.loading.set(false);
            return c as CommodityResponse | null;
          }),
          catchError((err) => handleDetailLoadError(err, { notFound: this.notFound, loadError: this.loadError }, this.loading)),
        );
      }),
    ),
    { initialValue: null },
  );

  constructor() {
    afterNextRender(() => {
      this.pageHeaderService.setActions(this.headerActionsTpl());
      this.pageHeaderService.setTitle(this.headerTitleTpl());
    });
    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      takeUntilDestroyed(),
    ).subscribe(() => {
      this.pageHeaderService.setActions(this.headerActionsTpl());
      this.pageHeaderService.setTitle(this.headerTitleTpl());
    });
  }

  ngOnDestroy(): void {
    this.pageHeaderService.clearActions();
    this.pageHeaderService.clearTitle();
  }

  /** Retry a failed load/refetch — same mechanism onSheetSaved uses to refresh. */
  retry(): void {
    this.refreshTrigger.update(v => v + 1);
  }

  openEdit(): void {
    this.sheetState.set('open');
  }

  onSheetSaved(): void {
    this.sheetState.set('closed');
    this.refreshTrigger.update(v => v + 1);
  }

  onSheetClosed(): void {
    this.sheetState.set('closed');
  }
}
