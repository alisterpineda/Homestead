import { Component, ChangeDetectionStrategy, inject, signal, OnDestroy, viewChild, afterNextRender, TemplateRef } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of, combineLatest, debounceTime } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUsers, lucidePlus, lucideChevronUp, lucideSearch } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmCardImports } from '@/shared/components/spartan/card';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { PartyTableComponent } from '../../components/party-table/party-table.component';
import { PartyDetailPanelComponent } from '../../components/party-detail-panel/party-detail-panel.component';
import { PaginationComponent } from '../../components/pagination/pagination.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getParties } from '@/api/generated/fn/documents/get-parties';
import { createParty } from '@/api/generated/fn/documents/create-party';
import type { PaginatedResponseOfGetPartiesResponse } from '@/api/generated/models/paginated-response-of-get-parties-response';

@Component({
  selector: 'app-party-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmEmptyImports,
    HlmInput,
    HlmLabel,
    HlmCardImports,
    PartyTableComponent,
    PartyDetailPanelComponent,
    PaginationComponent,
  ],
  providers: [provideIcons({ lucideUsers, lucidePlus, lucideChevronUp, lucideSearch })],
  template: `
    <ng-template #headerActions>
      <button hlmBtn size="sm" (click)="showCreateForm.set(!showCreateForm())">
        <ng-icon hlm [name]="showCreateForm() ? 'lucideChevronUp' : 'lucidePlus'" size="sm" />
        {{ showCreateForm() ? 'Cancel' : 'New Party' }}
      </button>
    </ng-template>

    <div>
      <!-- Create Form -->
      @if (showCreateForm()) {
        <hlm-card class="mb-6">
          <div hlmCardContent class="pt-6">
            <h3 class="mb-3 text-sm font-medium text-foreground">Create New Party</h3>
            <div class="flex flex-col gap-3 sm:flex-row">
              <div class="flex flex-1 flex-col gap-1.5">
                <label hlmLabel for="create-name">Name</label>
                <input
                  hlmInput
                  id="create-name"
                  placeholder="Party name"
                  [value]="createName()"
                  (input)="createName.set(asInputValue($event))"
                />
              </div>
              <div class="flex flex-1 flex-col gap-1.5">
                <label hlmLabel for="create-desc">Description</label>
                <input
                  hlmInput
                  id="create-desc"
                  placeholder="Optional description"
                  [value]="createDescription()"
                  (input)="createDescription.set(asInputValue($event))"
                />
              </div>
              <div class="flex items-end">
                <button
                  hlmBtn
                  [disabled]="!createName().trim() || creatingParty()"
                  (click)="handleCreate()"
                >
                  {{ creatingParty() ? 'Creating...' : 'Create' }}
                </button>
              </div>
            </div>
          </div>
        </hlm-card>
      }

      <div class="flex items-center gap-2 mb-4">
        <div class="relative">
          <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-2 top-1/2 -translate-y-1/2 text-muted-foreground" />
          <input
            hlmInput
            placeholder="Search parties..."
            [value]="searchText()"
            (input)="searchText.set(asInputValue($event))"
            class="pl-8 h-8 w-[200px]"
          />
        </div>
      </div>

      @if (loading()) {
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
        @if (result.items.length === 0) {
          <section hlmEmpty>
            <hlm-empty-media variant="icon">
              <ng-icon hlm name="lucideUsers" size="xl" />
            </hlm-empty-media>
            <div hlmEmptyHeader>
              <h3 hlmEmptyTitle>No parties found</h3>
              <p hlmEmptyDescription>
                {{ searchText() ? 'No parties match your search.' : 'Create your first party to get started.' }}
              </p>
            </div>
            @if (!searchText()) {
              <button hlmBtn (click)="showCreateForm.set(true)">
                <ng-icon hlm name="lucidePlus" size="sm" />
                New Party
              </button>
            }
          </section>
        } @else {
          <app-party-table
            [parties]="result.items"
            [selectedPartyId]="selectedPartyId()"
            (partySelect)="selectedPartyId.set($event)"
          />
          <app-pagination
            [page]="+result.page"
            [totalPages]="+(result.totalPages ?? 0)"
            [totalCount]="+result.totalCount"
            [pageSize]="+result.pageSize"
            (pageChange)="onPageChange($event)"
          />
        }
      }

      <app-party-detail-panel
        [partyId]="selectedPartyId()"
        (closed)="selectedPartyId.set(null)"
        (refreshList)="onRefresh()"
      />
    </div>
  `,
})
export class PartyListComponent implements OnDestroy {
  selectedPartyId = signal<string | null>(null);
  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  searchText = signal('');

  showCreateForm = signal(false);
  createName = signal('');
  createDescription = signal('');
  creatingParty = signal(false);

  private headerActionsRef = viewChild<TemplateRef<unknown>>('headerActions');
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private pageHeaderService = inject(PageHeaderService);

  constructor() {
    afterNextRender(() => {
      const tpl = this.headerActionsRef();
      if (tpl) {
        this.pageHeaderService.setActions(tpl);
      }
    });
  }

  ngOnDestroy(): void {
    this.pageHeaderService.clearActions();
  }

  private page = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('page') ?? 1))),
    { initialValue: 1 },
  );

  private debouncedSearch = toSignal(
    toObservable(this.searchText).pipe(debounceTime(300)),
    { initialValue: '' },
  );

  data = toSignal(
    combineLatest([
      toObservable(this.page),
      toObservable(this.refreshTrigger),
      toObservable(this.debouncedSearch),
    ]).pipe(
      switchMap(([page, , search]) => {
        this.loading.set(true);
        this.error.set(false);
        return getParties(this.http, this.apiConfig.rootUrl, {
          Page: page,
          PageSize: 10,
          Search: search || undefined,
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

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onPageChange(newPage: number): void {
    this.router.navigate(['/documents/parties'], { queryParams: { page: newPage } });
  }

  onRefresh(): void {
    this.refreshTrigger.update((v) => v + 1);
  }

  handleCreate(): void {
    const name = this.createName().trim();
    if (!name) return;
    this.creatingParty.set(true);
    createParty(this.http, this.apiConfig.rootUrl, {
      body: { name, description: this.createDescription().trim() || null },
    }).subscribe({
      next: () => {
        this.creatingParty.set(false);
        this.createName.set('');
        this.createDescription.set('');
        this.showCreateForm.set(false);
        this.onRefresh();
      },
      error: () => {
        this.creatingParty.set(false);
      },
    });
  }
}
