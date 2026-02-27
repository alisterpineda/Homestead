import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of, combineLatest } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUsers, lucidePlus } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { PartyTableComponent } from '../../components/party-table/party-table.component';
import { CreatePartyDialogComponent } from '../../components/create-party-dialog/create-party-dialog.component';
import { PaginationComponent } from '../../components/pagination/pagination.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getParties } from '@/api/generated/fn/documents/get-parties';
import type { PaginatedResponseOfGetPartiesResponse } from '@/api/generated/models/paginated-response-of-get-parties-response';

@Component({
  selector: 'app-party-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    PartyTableComponent,
    CreatePartyDialogComponent,
    PaginationComponent,
  ],
  providers: [provideIcons({ lucideUsers, lucidePlus })],
  template: `
    <div>
      @if (loading() && !data()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (error()) {
        <section hlmEmpty>
          <hlm-empty-media variant="icon">
            <ng-icon hlm name="lucideUsers" size="xl" />
          </hlm-empty-media>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>Failed to load parties</h3>
            <p hlmEmptyDescription>There was an error loading the party list. Please try again.</p>
          </div>
        </section>
      }

      @if (data(); as result) {
        @if (result.items.length === 0 && !loading()) {
          <section hlmEmpty>
            <hlm-empty-media variant="icon">
              <ng-icon hlm name="lucideUsers" size="xl" />
            </hlm-empty-media>
            <div hlmEmptyHeader>
              <h3 hlmEmptyTitle>No parties yet</h3>
              <p hlmEmptyDescription>Create your first party to get started.</p>
            </div>
            <app-create-party-dialog (created)="onRefresh()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              New Party
            </app-create-party-dialog>
          </section>
        } @else {
          <div class="flex items-center gap-2 mb-4">
            <div class="ml-auto flex items-center gap-2">
              <app-create-party-dialog triggerSize="sm" (created)="onRefresh()">
                <ng-icon hlm name="lucidePlus" size="sm" />
                New Party
              </app-create-party-dialog>
            </div>
          </div>

          <app-party-table
            [parties]="result.items"
            (partySelect)="onPartySelect($event)"
          />
          <app-pagination
            [page]="+result.page"
            [totalPages]="+(result.totalPages ?? 0)"
            [totalCount]="+result.totalCount"
            [pageSize]="+result.pageSize"
            (pageChange)="onPageChange($event)"
            (pageSizeChange)="onPageSizeChange($event)"
          />
        }
      }
    </div>
  `,
})
export class PartyListComponent {
  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  private page = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('page') ?? 1))),
    { initialValue: 1 },
  );

  private pageSize = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('pageSize') ?? 10))),
    { initialValue: 10 },
  );

  data = toSignal(
    combineLatest([
      toObservable(this.page),
      toObservable(this.pageSize),
      toObservable(this.refreshTrigger),
    ]).pipe(
      switchMap(([page, pageSize]) => {
        this.loading.set(true);
        this.error.set(false);
        return getParties(this.http, this.apiConfig.rootUrl, {
          Page: page,
          PageSize: pageSize,
        }).pipe(
          map((r) => {
            this.loading.set(false);
            return r.body as PaginatedResponseOfGetPartiesResponse;
          }),
          catchError(() => {
            this.loading.set(false);
            this.error.set(true);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  onPageChange(newPage: number): void {
    this.router.navigate(['/documents/parties'], {
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
    });
  }

  onPageSizeChange(newSize: number): void {
    this.router.navigate(['/documents/parties'], {
      queryParams: { page: 1, pageSize: newSize },
      queryParamsHandling: 'merge',
    });
  }

  onPartySelect(id: string): void {
    this.router.navigate(['/documents/parties', id]);
  }

  onRefresh(): void {
    this.refreshTrigger.update((v) => v + 1);
  }
}
