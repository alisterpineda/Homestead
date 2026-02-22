import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of, combineLatest, debounceTime } from 'rxjs';
import { LucideAngularModule } from 'lucide-angular';
import { Users, Plus, ChevronDown, ChevronUp } from 'lucide-angular';
import { PageHeaderComponent } from '@/shared/components/ui/page-header/page-header.component';
import { ButtonComponent } from '@/shared/components/ui/button/button.component';
import { SpinnerComponent } from '@/shared/components/ui/spinner/spinner.component';
import { EmptyStateComponent } from '@/shared/components/ui/empty-state/empty-state.component';
import { InputComponent } from '@/shared/components/ui/input/input.component';
import { CardComponent } from '@/shared/components/ui/card/card.component';
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
    LucideAngularModule,
    PageHeaderComponent,
    ButtonComponent,
    SpinnerComponent,
    EmptyStateComponent,
    InputComponent,
    CardComponent,
    PartyTableComponent,
    PartyDetailPanelComponent,
    PaginationComponent,
  ],
  template: `
    <div>
      <app-page-header title="Parties" description="Manage parties associated with your documents">
        <button action (click)="showCreateForm.set(!showCreateForm())">
          <app-button>
            <lucide-icon [img]="showCreateForm() ? chevronUpIcon : plusIcon" [size]="16" />
            {{ showCreateForm() ? 'Cancel' : 'New Party' }}
          </app-button>
        </button>
      </app-page-header>

      <!-- Create Form -->
      @if (showCreateForm()) {
        <app-card className="mb-6">
          <h3 class="mb-3 text-sm font-medium text-text-primary">Create New Party</h3>
          <div class="flex flex-col gap-3 sm:flex-row">
            <div class="flex-1">
              <app-input
                label="Name"
                placeholder="Party name"
                [value]="createName()"
                (valueChange)="createName.set($event)"
              />
            </div>
            <div class="flex-1">
              <app-input
                label="Description"
                placeholder="Optional description"
                [value]="createDescription()"
                (valueChange)="createDescription.set($event)"
              />
            </div>
            <div class="flex items-end">
              <app-button
                [disabled]="!createName().trim() || creatingParty()"
                (click)="handleCreate()"
              >
                {{ creatingParty() ? 'Creating...' : 'Create' }}
              </app-button>
            </div>
          </div>
        </app-card>
      }

      <!-- Search -->
      <div class="mb-4">
        <app-input
          placeholder="Search parties..."
          [value]="searchText()"
          (valueChange)="searchText.set($event)"
        />
      </div>

      @if (loading()) {
        <div class="flex justify-center py-16">
          <app-spinner size="lg" />
        </div>
      }

      @if (error()) {
        <app-empty-state
          [icon]="usersIcon"
          title="Failed to load parties"
          description="There was an error loading the party list. Please try again."
        />
      }

      @if (data(); as result) {
        @if (result.items.length === 0) {
          <app-empty-state
            [icon]="usersIcon"
            title="No parties found"
            [description]="searchText() ? 'No parties match your search.' : 'Create your first party to get started.'"
          >
            @if (!searchText()) {
              <app-button (click)="showCreateForm.set(true)">
                <lucide-icon [img]="plusIcon" [size]="16" />
                New Party
              </app-button>
            }
          </app-empty-state>
        } @else {
          <app-party-table
            [parties]="result.items"
            [selectedPartyId]="selectedPartyId()"
            (partySelect)="selectedPartyId.set($event)"
          />
          <app-pagination
            [page]="+result.page"
            [totalPages]="+(result.totalPages ?? 0)"
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
export class PartyListComponent {
  usersIcon = Users;
  plusIcon = Plus;
  chevronUpIcon = ChevronUp;

  selectedPartyId = signal<string | null>(null);
  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  searchText = signal('');

  showCreateForm = signal(false);
  createName = signal('');
  createDescription = signal('');
  creatingParty = signal(false);

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

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
