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
import { switchMap, map, filter, combineLatest } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePencil, lucideCircleAlert } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmBreadCrumbImports } from '@/shared/components/spartan/breadcrumb';
import { HlmTabsImports } from '@/shared/components/spartan/tabs';
import { HlmBadgeImports } from '@/shared/components/spartan/badge';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { CommodityInfoTabComponent } from './commodity-info-tab.component';
import { CommodityPricesTabComponent } from './commodity-prices-tab.component';
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
    HlmBreadCrumbImports,
    HlmTabsImports,
    HlmBadgeImports,
    CommodityInfoTabComponent,
    CommodityPricesTabComponent,
    EditCommoditySheetComponent,
  ],
  providers: [provideIcons({ lucidePencil, lucideCircleAlert })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <ng-template #headerActions>
      @if (commodity(); as c) {
        @if (!c.isSystemManaged) {
          <button hlmBtn variant="outline" size="sm" (click)="openEdit()">
            <ng-icon hlm name="lucidePencil" size="sm" />
            Edit
          </button>
        }
      }
    </ng-template>

    <div class="flex min-h-0 flex-1 flex-col gap-4 p-4 md:p-6">
      @if (commodity(); as c) {
        <nav>
          <ol hlmBreadcrumbList>
            <li hlmBreadcrumbItem>
              <a hlmBreadcrumbLink link="/finances/commodities">Commodities</a>
            </li>
            <li hlmBreadcrumbSeparator></li>
            <li hlmBreadcrumbItem>
              <span hlmBreadcrumbPage>{{ c.symbol }}</span>
            </li>
          </ol>
        </nav>

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

        <hlm-tabs tab="info" class="flex flex-1 flex-col">
          <hlm-tabs-list variant="line">
            <button hlmTabsTrigger="info">Info</button>
            <button hlmTabsTrigger="prices">Prices</button>
          </hlm-tabs-list>
          <div hlmTabsContent="info" class="pt-4">
            <app-commodity-info-tab [commodity]="c" />
          </div>
          <div hlmTabsContent="prices" class="pt-4">
            <app-commodity-prices-tab [commodity]="c" />
          </div>
        </hlm-tabs>
      } @else if (loading()) {
        <div class="flex justify-center py-16"><hlm-spinner /></div>
      } @else {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Commodity not found</div>
            <div hlmEmptyDescription>The commodity couldn't be loaded.</div>
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
  error = signal(false);
  sheetState = signal<BrnDialogState>('closed');

  private headerActionsTpl = viewChild.required<TemplateRef<unknown>>('headerActions');

  private id = toSignal(
    this.route.paramMap.pipe(map(p => p.get('id') ?? '')),
    { initialValue: '' },
  );

  commodity = toSignal<CommodityResponse | null, CommodityResponse | null>(
    combineLatest([toObservable(this.id), toObservable(this.refreshTrigger)]).pipe(
      switchMap(([id]) => {
        if (!id) return [null];
        this.loading.set(true);
        this.error.set(false);
        return this.commodityApi.getCommodity({ id }).pipe(
          map(c => {
            this.loading.set(false);
            return c as CommodityResponse | null;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  constructor() {
    afterNextRender(() => {
      this.pageHeaderService.setActions(this.headerActionsTpl());
    });
    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      takeUntilDestroyed(),
    ).subscribe(() => {
      this.pageHeaderService.setActions(this.headerActionsTpl());
    });
  }

  ngOnDestroy(): void {
    this.pageHeaderService.clearActions();
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
