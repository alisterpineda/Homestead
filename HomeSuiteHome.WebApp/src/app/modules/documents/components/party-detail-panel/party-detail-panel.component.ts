import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  signal,
  inject,
  effect,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, of, map, catchError, combineLatest } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideX,
  lucideTrash2,
  lucidePencil,
  lucideCheck,
  lucideCalendar,
  lucideFileText,
  lucideAlignLeft,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/components/ui/icon';
import { HlmButton } from '@/components/ui/button';
import { HlmSpinner } from '@/components/ui/spinner';
import { HlmTabsImports } from '@/components/ui/tabs';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getParty } from '@/api/generated/fn/documents/get-party';
import { updateParty } from '@/api/generated/fn/documents/update-party';
import { deleteParty } from '@/api/generated/fn/documents/delete-party';
import type { GetPartyResponse } from '@/api/generated/models/get-party-response';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-party-detail-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmTabsImports,
  ],
  providers: [
    provideIcons({
      lucideX,
      lucideTrash2,
      lucidePencil,
      lucideCheck,
      lucideCalendar,
      lucideFileText,
      lucideAlignLeft,
    }),
  ],
  host: {
    '(document:keydown.escape)': 'onEscape()',
  },
  template: `
    @if (partyId()) {
      <div class="fixed inset-0 z-50" role="dialog" aria-modal="true">
        <div class="absolute inset-0 bg-black/50" (click)="closed.emit()"></div>
        <div class="absolute inset-y-0 right-0 flex w-full max-w-md flex-col border-l border-border bg-background shadow-xl">
          @if (loading()) {
            <div class="flex h-full items-center justify-center">
              <hlm-spinner />
            </div>
          }

          @if (party()) {
            <!-- Header -->
            <div class="flex items-center gap-3 border-b border-border px-4 py-3">
              <div class="min-w-0 flex-1">
                @if (editing()) {
                  <input
                    [value]="editName()"
                    (input)="editName.set(asInputValue($event))"
                    class="w-full rounded-md border border-input bg-background px-3 py-1.5 text-lg font-semibold text-foreground transition-colors focus:border-ring focus:outline-none focus:ring-2 focus:ring-ring/20"
                  />
                } @else {
                  <h2 class="truncate text-lg font-semibold text-foreground">
                    {{ party()!.name }}
                  </h2>
                }
              </div>
              <button
                hlmBtn
                variant="ghost"
                size="icon-sm"
                (click)="closed.emit()"
                aria-label="Close panel"
              >
                <ng-icon hlm name="lucideX" size="sm" />
              </button>
            </div>

            <!-- Actions -->
            <div class="flex gap-2 border-b border-border px-4 py-3">
              @if (editing()) {
                <button hlmBtn (click)="handleSave()" [disabled]="saving()">
                  <ng-icon hlm name="lucideCheck" size="sm" />
                  {{ saving() ? 'Saving...' : 'Save' }}
                </button>
                <button hlmBtn variant="secondary" (click)="handleCancelEdit()" [disabled]="saving()">
                  <ng-icon hlm name="lucideX" size="sm" />
                  Cancel
                </button>
              } @else {
                <button hlmBtn variant="secondary" (click)="editing.set(true)">
                  <ng-icon hlm name="lucidePencil" size="sm" />
                  Edit
                </button>
                <button
                  hlmBtn
                  variant="ghost"
                  (click)="handleDelete()"
                  [disabled]="deleting()"
                  class="text-destructive hover:bg-destructive/10 hover:text-destructive"
                >
                  <ng-icon hlm name="lucideTrash2" size="sm" />
                  {{ deleting() ? 'Deleting...' : 'Delete' }}
                </button>
              }
            </div>

            <!-- Tabs -->
            <hlm-tabs tab="info" class="flex flex-1 flex-col overflow-hidden">
              <hlm-tabs-list class="mx-4 mt-3">
                <button hlmTabsTrigger="info">Info</button>
                <button hlmTabsTrigger="documents">Documents</button>
              </hlm-tabs-list>
              <div hlmTabsContent="info" class="flex-1 overflow-y-auto px-4 py-3">
                <div class="space-y-4">
                  <!-- Description -->
                  <div class="flex gap-3">
                    <div class="flex shrink-0 items-start gap-2 pt-0.5 text-muted-foreground">
                      <ng-icon hlm name="lucideAlignLeft" size="sm" />
                      <span class="w-16 text-sm font-medium">Description</span>
                    </div>
                    <div class="min-w-0 flex-1">
                      @if (editing()) {
                        <textarea
                          [value]="editDescription()"
                          (input)="editDescription.set(asTextareaValue($event))"
                          rows="3"
                          class="w-full rounded-md border border-input bg-background px-3 py-2 text-sm text-foreground transition-colors focus:border-ring focus:outline-none focus:ring-2 focus:ring-ring/20"
                        ></textarea>
                      } @else {
                        <span class="text-sm text-foreground">
                          {{ party()!.description || 'No description' }}
                        </span>
                      }
                    </div>
                  </div>

                  <!-- Created -->
                  <div class="flex gap-3">
                    <div class="flex shrink-0 items-start gap-2 pt-0.5 text-muted-foreground">
                      <ng-icon hlm name="lucideCalendar" size="sm" />
                      <span class="w-16 text-sm font-medium">Created</span>
                    </div>
                    <div class="min-w-0 flex-1">
                      <span class="text-sm text-foreground">
                        {{ formatDate(party()!.createdAt) }}
                      </span>
                    </div>
                  </div>
                </div>
              </div>

              <div hlmTabsContent="documents" class="flex-1 overflow-y-auto px-4 py-3">
                @if (party()!.documents.length === 0) {
                  <p class="text-sm text-muted-foreground">No linked documents</p>
                } @else {
                  <div class="space-y-2">
                    @for (doc of party()!.documents; track doc.id) {
                      <a
                        [routerLink]="['/documents']"
                        [queryParams]="{ selected: doc.id }"
                        class="flex items-center gap-2 rounded-md px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent"
                      >
                        <ng-icon hlm name="lucideFileText" size="sm" class="shrink-0 text-muted-foreground" />
                        <span class="truncate">{{ doc.name }}</span>
                        <span class="ml-auto shrink-0 text-xs text-muted-foreground">{{ doc.contentType }}</span>
                      </a>
                    }
                  </div>
                }
              </div>
            </hlm-tabs>
          }
        </div>
      </div>
    }
  `,
})
export class PartyDetailPanelComponent {
  partyId = input<string | null>(null);
  closed = output<void>();
  refreshList = output<void>();

  editing = signal(false);
  editName = signal('');
  editDescription = signal('');
  saving = signal(false);
  deleting = signal(false);
  loading = signal(false);
  private refreshTrigger = signal(0);

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  private partyData = toSignal(
    combineLatest([toObservable(this.partyId), toObservable(this.refreshTrigger)]).pipe(
      switchMap(([id]) => {
        if (!id) return of(null);
        this.loading.set(true);
        return getParty(this.http, this.apiConfig.rootUrl, { id }).pipe(
          map((r) => {
            this.loading.set(false);
            return r.body as GetPartyResponse;
          }),
          catchError(() => {
            this.loading.set(false);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  party = this.partyData;

  constructor() {
    effect(() => {
      const p = this.party();
      if (p) {
        this.editName.set(p.name);
        this.editDescription.set(p.description ?? '');
      }
    });

    effect(() => {
      this.partyId();
      this.editing.set(false);
    });
  }

  onEscape(): void {
    if (this.partyId()) {
      this.closed.emit();
    }
  }

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  asTextareaValue(event: Event): string {
    return (event.target as HTMLTextAreaElement).value;
  }

  handleSave(): void {
    const id = this.partyId();
    if (!id) return;
    this.saving.set(true);
    updateParty(this.http, this.apiConfig.rootUrl, {
      id,
      body: { name: this.editName(), description: this.editDescription() || null },
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.editing.set(false);
        this.refreshList.emit();
        this.refreshTrigger.update((v) => v + 1);
        toast.success('Party updated.');
      },
      error: () => {
        this.saving.set(false);
        toast.error('Failed to save changes.');
      },
    });
  }

  handleCancelEdit(): void {
    this.editing.set(false);
    const p = this.party();
    if (p) {
      this.editName.set(p.name);
      this.editDescription.set(p.description ?? '');
    }
  }

  handleDelete(): void {
    const id = this.partyId();
    if (!id) return;
    this.deleting.set(true);
    deleteParty(this.http, this.apiConfig.rootUrl, { id }).subscribe({
      next: () => {
        this.deleting.set(false);
        this.refreshList.emit();
        this.closed.emit();
        toast.success('Party deleted.');
      },
      error: () => {
        this.deleting.set(false);
        toast.error('Failed to delete party.');
      },
    });
  }
}
