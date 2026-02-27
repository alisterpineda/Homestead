import {
  Component,
  ChangeDetectionStrategy,
  signal,
  inject,
  effect,
  computed,
  OnDestroy,
  viewChild,
  afterNextRender,
  TemplateRef,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, RouterLink, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, of, map, catchError, combineLatest } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTrash2, lucideSave, lucideFileText } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmBreadCrumbImports } from '@/shared/components/spartan/breadcrumb';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmFormFieldImports } from '@/shared/components/spartan/form-field';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmTextareaImports } from '@/shared/components/spartan/textarea';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getParty } from '@/api/generated/fn/documents/get-party';
import { updateParty } from '@/api/generated/fn/documents/update-party';
import { deleteParty } from '@/api/generated/fn/documents/delete-party';
import type { GetPartyResponse } from '@/api/generated/models/get-party-response';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-party-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmBreadCrumbImports,
    HlmInput,
    HlmFormFieldImports,
    HlmLabel,
    HlmTextareaImports,
    HlmAlertDialogImports,
  ],
  providers: [
    provideIcons({
      lucideTrash2,
      lucideSave,
      lucideFileText,
    }),
  ],
  template: `
    <ng-template #headerTitle>
      <nav hlmBreadcrumb class="min-w-0">
        <ol hlmBreadcrumbList class="flex-nowrap overflow-hidden">
          <li hlmBreadcrumbItem>
            <a hlmBreadcrumbLink link="/documents/parties">Parties</a>
          </li>
          <li hlmBreadcrumbSeparator></li>
          <li hlmBreadcrumbItem class="min-w-0">
            <span hlmBreadcrumbPage
                  [title]="party()?.name ?? 'Party'"
                  class="truncate max-w-[150px] sm:max-w-[250px] md:max-w-[350px]">
              {{ party()?.name ?? 'Party' }}
            </span>
          </li>
        </ol>
      </nav>
    </ng-template>

    @if (loading()) {
      <div class="flex h-full items-center justify-center">
        <hlm-spinner />
      </div>
    }

    @if (party(); as p) {
      <div class="flex items-center gap-2 mb-4">
        <div class="ml-auto flex items-center gap-2">
          <button hlmBtn size="sm" (click)="handleSave()" [disabled]="!hasChanges() || saving()">
            <ng-icon hlm name="lucideSave" size="sm" />
            {{ saving() ? 'Saving...' : 'Save' }}
          </button>
          <hlm-alert-dialog>
            <button hlmBtn variant="destructive" size="sm" hlmAlertDialogTrigger>
              <ng-icon hlm name="lucideTrash2" size="sm" />
              Delete
            </button>
            <hlm-alert-dialog-content *hlmAlertDialogPortal>
              <div hlmAlertDialogHeader>
                <h2 hlmAlertDialogTitle>Delete party?</h2>
                <p hlmAlertDialogDescription>
                  This action cannot be undone. This party will be permanently deleted
                  and removed from all associated documents.
                </p>
              </div>
              <div hlmAlertDialogFooter>
                <button hlmAlertDialogCancel>Cancel</button>
                <button hlmAlertDialogAction variant="destructive" (click)="handleDelete()" [disabled]="deleting()">
                  {{ deleting() ? 'Deleting...' : 'Delete' }}
                </button>
              </div>
            </hlm-alert-dialog-content>
          </hlm-alert-dialog>
        </div>
      </div>

      <div class="max-w-2xl space-y-6">
        <!-- Name -->
        <hlm-form-field>
          <label hlmLabel for="party-name">Name</label>
          <input
            hlmInput
            id="party-name"
            [value]="editName()"
            (input)="editName.set(asInputValue($event))"
          />
        </hlm-form-field>

        <!-- Description -->
        <hlm-form-field>
          <label hlmLabel for="party-desc">Description</label>
          <textarea
            hlmTextarea
            id="party-desc"
            rows="3"
            placeholder="No description"
            [value]="editDescription()"
            (input)="editDescription.set(asInputValue($event))"
          ></textarea>
        </hlm-form-field>

        <!-- Created -->
        <div class="space-y-2">
          <label hlmLabel>Created</label>
          <p class="text-sm text-muted-foreground">
            {{ formatDate(p.createdAt) }}
          </p>
        </div>

        <!-- Linked Documents -->
        @if (p.documents.length > 0) {
          <div class="space-y-2">
            <label hlmLabel>Linked Documents</label>
            <div class="space-y-1">
              @for (doc of p.documents; track doc.id) {
                <a
                  [routerLink]="['/documents', doc.id]"
                  class="flex items-center gap-2 rounded-md px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent"
                >
                  <ng-icon hlm name="lucideFileText" size="sm" class="shrink-0 text-muted-foreground" />
                  <span class="truncate">{{ doc.name }}</span>
                  <span class="ml-auto shrink-0 text-xs text-muted-foreground">{{ doc.contentType }}</span>
                </a>
              }
            </div>
          </div>
        }
      </div>
    }
  `,
  host: {
    class: 'flex flex-col h-full',
  },
})
export class PartyDetailPageComponent implements OnDestroy {
  editName = signal('');
  editDescription = signal('');
  saving = signal(false);
  deleting = signal(false);
  loading = signal(false);
  refreshTrigger = signal(0);

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private pageHeaderService = inject(PageHeaderService);

  private headerTitleRef = viewChild<TemplateRef<unknown>>('headerTitle');

  private partyId = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('id'))),
    { initialValue: null },
  );

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

  hasChanges = computed(() => {
    const p = this.party();
    if (!p) return false;
    if (this.editName() !== p.name) return true;
    if (this.editDescription() !== (p.description ?? '')) return true;
    return false;
  });

  constructor() {
    effect(() => {
      const p = this.party();
      if (p) {
        this.editName.set(p.name);
        this.editDescription.set(p.description ?? '');
      }
    });

    afterNextRender(() => {
      const tpl = this.headerTitleRef();
      if (tpl) {
        this.pageHeaderService.setTitle(tpl);
      }
    });
  }

  ngOnDestroy(): void {
    this.pageHeaderService.clearTitle();
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
        this.refreshTrigger.update((v) => v + 1);
        toast.success('Party updated.');
      },
      error: () => {
        this.saving.set(false);
        toast.error('Failed to save changes.');
      },
    });
  }

  handleDelete(): void {
    const id = this.partyId();
    if (!id) return;
    this.deleting.set(true);
    deleteParty(this.http, this.apiConfig.rootUrl, { id }).subscribe({
      next: () => {
        toast.success('Party deleted.');
        this.router.navigate(['/documents/parties']);
      },
      error: () => {
        this.deleting.set(false);
        toast.error('Failed to delete party.');
      },
    });
  }
}
