import {
  Component,
  ChangeDetectionStrategy,
  signal,
  inject,
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
import { lucideTrash2, lucideFileText } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmBreadCrumbImports } from '@/shared/components/spartan/breadcrumb';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getTags } from '@/api/generated/fn/documents/get-tags';
import { deleteTag } from '@/api/generated/fn/documents/delete-tag';
import { updateTag } from '@/api/generated/fn/documents/update-tag';
import { getDocuments } from '@/api/generated/fn/documents/get-documents';
import type { GetTagsResponse } from '@/api/generated/models/get-tags-response';
import type { PaginatedResponseOfGetDocumentsResponse } from '@/api/generated/models/paginated-response-of-get-documents-response';
import { ColorPickerComponent } from '../../components/color-picker/color-picker.component';
import { TagBadgeComponent } from '../../components/tag-badge/tag-badge.component';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-tag-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmBreadCrumbImports,
    HlmLabel,
    HlmAlertDialogImports,
    ColorPickerComponent,
    TagBadgeComponent,
  ],
  providers: [
    provideIcons({
      lucideTrash2,
      lucideFileText,
    }),
  ],
  template: `
    <ng-template #headerTitle>
      <nav hlmBreadcrumb class="min-w-0">
        <ol hlmBreadcrumbList class="flex-nowrap overflow-hidden">
          <li hlmBreadcrumbItem>
            <a hlmBreadcrumbLink link="/documents/tags">Tags</a>
          </li>
          <li hlmBreadcrumbSeparator></li>
          <li hlmBreadcrumbItem class="min-w-0">
            <span hlmBreadcrumbPage
                  [title]="tag()?.path ?? 'Tag'"
                  class="truncate max-w-[150px] sm:max-w-[250px] md:max-w-[350px]">
              {{ tag()?.path ?? 'Tag' }}
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

    @if (tag(); as t) {
      <div class="flex items-center gap-2 mb-4">
        <div class="ml-auto flex items-center gap-2">
          <hlm-alert-dialog>
            <button hlmBtn variant="destructive" size="sm" hlmAlertDialogTrigger>
              <ng-icon hlm name="lucideTrash2" size="sm" />
              Delete
            </button>
            <hlm-alert-dialog-content *hlmAlertDialogPortal>
              <div hlmAlertDialogHeader>
                <h2 hlmAlertDialogTitle>Delete tag?</h2>
                <p hlmAlertDialogDescription>
                  This action cannot be undone. Any child tags will be re-parented to this tag's parent.
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
        <div class="space-y-2">
          <label hlmLabel>Name</label>
          <p class="text-sm text-foreground">{{ t.name }}</p>
        </div>

        <!-- Path -->
        <div class="space-y-2">
          <label hlmLabel>Path</label>
          <p class="text-sm text-foreground">{{ t.path }}</p>
        </div>

        <!-- Color -->
        <div class="space-y-2">
          <label hlmLabel>Color</label>
          <div class="flex items-center gap-3">
            <app-color-picker [color]="t.color ?? null" (colorChange)="handleColorChange($event)" />
          </div>
          @if (t.color) {
            <div class="mt-2">
              <span class="text-xs text-muted-foreground mr-2">Preview:</span>
              <app-tag-badge [path]="t.path" [color]="t.color" />
            </div>
          }
        </div>

        <!-- Child Tags -->
        @if (childTags().length > 0) {
          <div class="space-y-2">
            <label hlmLabel>Child Tags</label>
            <div class="space-y-1">
              @for (child of childTags(); track child.id) {
                <a
                  [routerLink]="['/documents/tags', child.id]"
                  class="flex items-center gap-2 rounded-md px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent"
                >
                  @if (child.color) {
                    <span class="h-3 w-3 shrink-0 rounded-full" [style.background-color]="child.color"></span>
                  }
                  <span class="truncate">{{ child.name }}</span>
                  <span class="ml-auto shrink-0 text-xs text-muted-foreground">{{ child.path }}</span>
                </a>
              }
            </div>
          </div>
        }

        <!-- Linked Documents -->
        @if (documents().length > 0) {
          <div class="space-y-2">
            <label hlmLabel>Linked Documents</label>
            <div class="space-y-1">
              @for (doc of documents(); track doc.id) {
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
export class TagDetailPageComponent implements OnDestroy {
  deleting = signal(false);
  loading = signal(false);
  private refreshTrigger = signal(0);

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private pageHeaderService = inject(PageHeaderService);

  private headerTitleRef = viewChild<TemplateRef<unknown>>('headerTitle');

  private tagId = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('id'))),
    { initialValue: null },
  );

  private allTags = toSignal(
    combineLatest([toObservable(this.tagId), toObservable(this.refreshTrigger)]).pipe(
      switchMap(([id]) => {
        if (!id) return of(null);
        this.loading.set(true);
        return getTags(this.http, this.apiConfig.rootUrl, { IncludeChildren: true }).pipe(
          map((r) => {
            this.loading.set(false);
            return r.body as GetTagsResponse[];
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

  tag = computed(() => {
    const tags = this.allTags();
    const id = this.tagId();
    if (!tags || !id) return null;
    return tags.find((t) => t.id === id) ?? null;
  });

  childTags = computed(() => {
    const tags = this.allTags();
    const id = this.tagId();
    if (!tags || !id) return [];
    return tags.filter((t) => t.parentId === id);
  });

  private documentsData = toSignal(
    toObservable(this.tagId).pipe(
      switchMap((id) => {
        if (!id) return of(null);
        return getDocuments(this.http, this.apiConfig.rootUrl, {
          TagIds: [id],
          IncludeChildTags: false,
          Page: 1,
          PageSize: 100,
        }).pipe(
          map((r) => r.body as PaginatedResponseOfGetDocumentsResponse),
          catchError(() => of(null)),
        );
      }),
    ),
    { initialValue: null },
  );

  documents = computed(() => this.documentsData()?.items ?? []);

  constructor() {
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

  handleColorChange(color: string | null): void {
    const id = this.tagId();
    if (!id) return;
    updateTag(this.http, this.apiConfig.rootUrl, {
      id,
      body: { color },
    }).subscribe({
      next: () => {
        toast.success('Tag color updated.');
        this.refreshTrigger.update((v) => v + 1);
      },
      error: () => {
        toast.error('Failed to update tag color.');
      },
    });
  }

  handleDelete(): void {
    const id = this.tagId();
    if (!id) return;
    this.deleting.set(true);
    deleteTag(this.http, this.apiConfig.rootUrl, { id }).subscribe({
      next: () => {
        toast.success('Tag deleted.');
        this.router.navigate(['/documents/tags']);
      },
      error: () => {
        this.deleting.set(false);
        toast.error('Failed to delete tag.');
      },
    });
  }
}
