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
import { LucideAngularModule } from 'lucide-angular';
import { X, Trash2, Pencil, Check, Calendar, FileText, AlignLeft } from 'lucide-angular';
import { SheetComponent } from '@/shared/components/ui/sheet/sheet.component';
import { TabsComponent, TabDefDirective } from '@/shared/components/ui/tabs/tabs.component';
import { ButtonComponent } from '@/shared/components/ui/button/button.component';
import { SpinnerComponent } from '@/shared/components/ui/spinner/spinner.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getParty } from '@/api/generated/fn/documents/get-party';
import { updateParty } from '@/api/generated/fn/documents/update-party';
import { deleteParty } from '@/api/generated/fn/documents/delete-party';
import type { GetPartyResponse } from '@/api/generated/models/get-party-response';

@Component({
  selector: 'app-party-detail-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    LucideAngularModule,
    SheetComponent,
    TabsComponent,
    TabDefDirective,
    ButtonComponent,
    SpinnerComponent,
  ],
  template: `
    <app-sheet [open]="!!partyId()" (closed)="closed.emit()">
      @if (loading()) {
        <div class="flex h-full items-center justify-center">
          <app-spinner size="lg" />
        </div>
      }

      @if (party()) {
        <div class="flex h-full flex-col">
          <!-- Header -->
          <div class="flex items-center gap-3 border-b border-border px-4 py-3">
            <div class="min-w-0 flex-1">
              @if (editing()) {
                <input
                  [value]="editName()"
                  (input)="editName.set(asInputValue($event))"
                  class="w-full rounded-lg border border-border bg-surface px-3 py-1.5 text-lg font-semibold text-text-primary transition-colors focus:border-accent-500 focus:outline-none focus:ring-2 focus:ring-accent-500/20"
                />
              } @else {
                <h2 class="truncate text-lg font-semibold text-text-primary">
                  {{ party()!.name }}
                </h2>
              }
            </div>
            <button
              (click)="closed.emit()"
              class="shrink-0 cursor-pointer rounded-lg p-1 text-text-secondary hover:bg-surface-raised hover:text-text-primary"
            >
              <lucide-icon [img]="icons.x" [size]="20" />
            </button>
          </div>

          <!-- Actions -->
          <div class="flex gap-2 border-b border-border px-4 py-3">
            @if (editing()) {
              <app-button variant="primary" (click)="handleSave()" [disabled]="saving()">
                <lucide-icon [img]="icons.check" [size]="16" />
                {{ saving() ? 'Saving...' : 'Save' }}
              </app-button>
              <app-button variant="secondary" (click)="handleCancelEdit()" [disabled]="saving()">
                <lucide-icon [img]="icons.x" [size]="16" />
                Cancel
              </app-button>
            } @else {
              <app-button variant="secondary" (click)="editing.set(true)">
                <lucide-icon [img]="icons.pencil" [size]="16" />
                Edit
              </app-button>
              <app-button
                variant="ghost"
                (click)="handleDelete()"
                [disabled]="deleting()"
                className="text-red-500 hover:bg-red-50 hover:text-red-600"
              >
                <lucide-icon [img]="icons.trash" [size]="16" />
                {{ deleting() ? 'Deleting...' : 'Delete' }}
              </app-button>
            }
          </div>

          <!-- Tabs -->
          <div class="flex-1 overflow-y-auto px-4 py-3">
            <app-tabs>
              <ng-template appTabDef="info" label="Info">
                <div class="space-y-4">
                  <!-- Description -->
                  <div class="flex gap-3">
                    <div class="flex shrink-0 items-start gap-2 pt-0.5 text-text-secondary">
                      <lucide-icon [img]="icons.alignLeft" [size]="16" />
                      <span class="w-16 text-sm font-medium">Description</span>
                    </div>
                    <div class="min-w-0 flex-1">
                      @if (editing()) {
                        <textarea
                          [value]="editDescription()"
                          (input)="editDescription.set(asTextareaValue($event))"
                          rows="3"
                          class="w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm text-text-primary transition-colors focus:border-accent-500 focus:outline-none focus:ring-2 focus:ring-accent-500/20"
                        ></textarea>
                      } @else {
                        <span class="text-sm text-text-primary">
                          {{ party()!.description || 'No description' }}
                        </span>
                      }
                    </div>
                  </div>

                  <!-- Created -->
                  <div class="flex gap-3">
                    <div class="flex shrink-0 items-start gap-2 pt-0.5 text-text-secondary">
                      <lucide-icon [img]="icons.calendar" [size]="16" />
                      <span class="w-16 text-sm font-medium">Created</span>
                    </div>
                    <div class="min-w-0 flex-1">
                      <span class="text-sm text-text-primary">
                        {{ formatDate(party()!.createdAt) }}
                      </span>
                    </div>
                  </div>
                </div>
              </ng-template>

              <ng-template appTabDef="documents" label="Documents">
                @if (party()!.documents.length === 0) {
                  <p class="text-sm text-text-muted">No linked documents</p>
                } @else {
                  <div class="space-y-2">
                    @for (doc of party()!.documents; track doc.id) {
                      <a
                        [routerLink]="['/documents']"
                        [queryParams]="{ selected: doc.id }"
                        class="flex items-center gap-2 rounded-lg px-3 py-2 text-sm text-text-primary transition-colors hover:bg-surface-raised"
                      >
                        <lucide-icon [img]="icons.fileText" [size]="16" class="shrink-0 text-text-secondary" />
                        <span class="truncate">{{ doc.name }}</span>
                        <span class="ml-auto shrink-0 text-xs text-text-muted">{{ doc.contentType }}</span>
                      </a>
                    }
                  </div>
                }
              </ng-template>
            </app-tabs>
          </div>
        </div>
      }
    </app-sheet>
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

  icons = { x: X, trash: Trash2, pencil: Pencil, check: Check, calendar: Calendar, fileText: FileText, alignLeft: AlignLeft };

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
      },
      error: () => {
        this.saving.set(false);
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
      },
      error: () => {
        this.deleting.set(false);
      },
    });
  }
}
