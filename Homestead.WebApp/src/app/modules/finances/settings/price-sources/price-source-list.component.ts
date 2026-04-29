import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { DatePipe } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRss } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { FinancesPriceSourcesApiService } from '@/api/services/finances-price-sources-api.service';
import type { GetPriceSourcesResponse } from '@/api/generated/models/get-price-sources-response';
import type { PriceSourceSummary } from '@/api/generated/models/price-source-summary';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';

type StatusPill = { label: string; variant: 'default' | 'destructive' | 'secondary' | 'outline' };

@Component({
  selector: 'app-price-source-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NgIcon, HlmIconImports, HlmSpinner, HlmEmptyImports, HlmTableImports, HlmBadge],
  providers: [provideIcons({ lucideRss })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-2">
      @if (loading()) {
        <div class="flex justify-center py-16"><hlm-spinner /></div>
      } @else if (error()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideRss" /></div>
            <div hlmEmptyTitle>Failed to load price sources</div>
            <div hlmEmptyDescription>Please try again.</div>
          </div>
        </div>
      } @else if (data(); as d) {
        <div class="overflow-clip rounded-md border">
          <div hlmTableContainer>
            <table hlmTable>
              <thead hlmTHead class="sticky top-0 z-10 bg-background">
                <tr hlmTr>
                  <th hlmTh>Name</th>
                  <th hlmTh class="w-32">Kind</th>
                  <th hlmTh class="w-32">State</th>
                  <th hlmTh class="hidden md:table-cell w-48">Last import</th>
                  <th hlmTh class="w-32">Status</th>
                  <th hlmTh class="w-24 text-right">Prices</th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (item of d.items; track item.source) {
                  <tr hlmTr class="cursor-pointer" (click)="goTo(item)">
                    <td hlmTd class="font-medium">{{ item.displayName }}</td>
                    <td hlmTd>
                      <span hlmBadge variant="secondary">{{ item.kind }}</span>
                    </td>
                    <td hlmTd>
                      @if (item.kind !== 'External') {
                        <span hlmBadge variant="outline">System</span>
                      } @else if (item.isEnabled) {
                        <span hlmBadge variant="default">Enabled</span>
                      } @else {
                        <span hlmBadge variant="outline">Disabled</span>
                      }
                    </td>
                    <td hlmTd class="hidden md:table-cell text-muted-foreground text-sm">
                      @if (item.lastImportAt) {
                        {{ item.lastImportAt | date:'medium' }}
                      } @else {
                        —
                      }
                    </td>
                    <td hlmTd>
                      @if (statusPill(item); as pill) {
                        <span hlmBadge [variant]="pill.variant">{{ pill.label }}</span>
                      }
                    </td>
                    <td hlmTd class="text-right tabular-nums">{{ item.priceCount }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      }
    </div>
  `,
})
export class PriceSourceListComponent {
  private api = inject(FinancesPriceSourcesApiService);
  private router = inject(Router);
  private errorHandler = inject(ErrorHandlerService);

  loading = signal(true);
  error = signal(false);
  data = signal<GetPriceSourcesResponse | null>(null);

  constructor() {
    this.api.list()
      .pipe(handleQueryError(this.loading, this.error, null))
      .subscribe(r => {
        this.loading.set(false);
        if (r) this.data.set(r);
      });
  }

  goTo(item: PriceSourceSummary): void {
    void this.router.navigate(['/finances/settings/price-sources', item.source]);
  }

  statusPill(item: PriceSourceSummary): StatusPill {
    if (!item.lastImportAt) return { label: 'Never run', variant: 'outline' };
    if (item.lastImportStatus === 'Failed') return { label: 'Failed', variant: 'destructive' };
    if (item.lastImportStatus === 'Success') return { label: 'Success', variant: 'default' };
    return { label: item.lastImportStatus ?? 'Never run', variant: 'secondary' };
  }
}
