import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTags, lucidePlus, lucideSearch, lucideTrash2, lucideX } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { TagTableComponent } from '../../components/tag-table/tag-table.component';
import { CreateTagDialogComponent } from '../../components/create-tag-dialog/create-tag-dialog.component';
import { EditTagSheetComponent } from '../../components/edit-tag-sheet/edit-tag-sheet.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getTags } from '@/api/generated/fn/documents/get-tags';
import { bulkDeleteTags } from '@/api/generated/fn/documents/bulk-delete-tags';
import type { GetTagsResponse } from '@/api/generated/models/get-tags-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-tag-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmInput,
    HlmButton,
    HlmAlertDialogImports,
    TagTableComponent,
    CreateTagDialogComponent,
    EditTagSheetComponent,
  ],
  providers: [provideIcons({ lucideTags, lucidePlus, lucideSearch, lucideTrash2, lucideX })],
  template: `
    <div>
      @if (loading() && !allTags()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (error()) {
        <section hlmEmpty>
          <hlm-empty-media variant="icon">
            <ng-icon hlm name="lucideTags" size="xl" />
          </hlm-empty-media>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>Failed to load tags</h3>
            <p hlmEmptyDescription>There was an error loading the tag list. Please try again.</p>
          </div>
        </section>
      }

      @if (allTags(); as tags) {
        @if (tags.length === 0 && !loading()) {
          <section hlmEmpty>
            <hlm-empty-media variant="icon">
              <ng-icon hlm name="lucideTags" size="xl" />
            </hlm-empty-media>
            <div hlmEmptyHeader>
              <h3 hlmEmptyTitle>No tags yet</h3>
              <p hlmEmptyDescription>Create your first tag to get started.</p>
            </div>
            <app-create-tag-dialog (created)="onRefresh()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              New Tag
            </app-create-tag-dialog>
          </section>
        } @else {
          <div class="relative">
            <!-- Normal toolbar -->
            <div [class.opacity-0]="hasSelection()" [class.pointer-events-none]="hasSelection()">
              <div class="flex items-center gap-2 mb-4">
                <div class="relative">
                  <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-2.5 top-1/2 -translate-y-1/2 text-muted-foreground" />
                  <input
                    hlmInput
                    type="text"
                    placeholder="Search tags..."
                    class="pl-8 h-8 w-[200px] text-sm"
                    [value]="searchInput()"
                    (input)="searchInput.set(asInputValue($event))"
                  />
                </div>
                <div class="ml-auto flex items-center gap-2">
                  <app-create-tag-dialog triggerSize="sm" (created)="onRefresh()">
                    <ng-icon hlm name="lucidePlus" size="sm" />
                    New Tag
                  </app-create-tag-dialog>
                </div>
              </div>
            </div>

            <!-- Selection toolbar -->
            <div class="absolute inset-x-0 top-0 flex items-center gap-2 h-8 bg-background" [class.opacity-0]="!hasSelection()" [class.pointer-events-none]="!hasSelection()">
              <span class="text-sm text-muted-foreground">
                {{ selectionCount() }} tag{{ selectionCount() > 1 ? 's' : '' }} selected
              </span>
              <div class="ml-auto flex items-center gap-2">
                <button hlmBtn variant="outline" size="sm" (click)="clearSelection()">
                  Clear selection
                </button>
                <hlm-alert-dialog>
                  <button hlmBtn variant="destructive" size="sm" hlmAlertDialogTrigger>
                    <ng-icon hlm name="lucideTrash2" size="sm" />
                    Delete
                  </button>
                  <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
                    <div hlmAlertDialogHeader>
                      <h2 hlmAlertDialogTitle>Delete {{ selectionCount() }} tag{{ selectionCount() > 1 ? 's' : '' }}?</h2>
                      <p hlmAlertDialogDescription>
                        This action cannot be undone. Any child tags will be re-parented to the deleted tag's parent.
                      </p>
                    </div>
                    <div hlmAlertDialogFooter>
                      <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
                      <button hlmAlertDialogAction variant="destructive" (click)="onBulkDelete(ctx)" [disabled]="deleting()">
                        {{ deleting() ? 'Deleting...' : 'Delete' }}
                      </button>
                    </div>
                  </hlm-alert-dialog-content>
                </hlm-alert-dialog>
              </div>
            </div>
          </div>

          <app-tag-table
            [tags]="filteredTags()"
            [selectedIds]="selectedTagIds()"
            (tagSelect)="onTagSelect($event)"
            (selectionChange)="selectedTagIds.set($event)"
          />
        }
      }
    </div>

    <app-edit-tag-sheet
      [tag]="selectedTag()"
      [allTags]="allTags() ?? []"
      [state]="sheetState()"
      (saved)="onSheetSaved()"
      (closed)="onSheetClosed()"
    />
  `,
})
export class TagListComponent {
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  searchInput = signal('');
  deleting = signal(false);

  // Selection state
  selectedTagIds = signal<Set<string>>(new Set());
  selectionCount = computed(() => this.selectedTagIds().size);
  hasSelection = computed(() => this.selectionCount() > 0);

  // Sheet state
  selectedTag = signal<GetTagsResponse | null>(null);
  sheetState = signal<BrnDialogState>('closed');

  allTags = toSignal(
    toObservable(this.refreshTrigger).pipe(
      switchMap(() => {
        this.loading.set(true);
        this.error.set(false);
        return getTags(this.http, this.apiConfig.rootUrl, {
          IncludeChildren: true,
        }).pipe(
          map((r) => {
            this.loading.set(false);
            return r.body as GetTagsResponse[];
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

  filteredTags = computed(() => {
    const tags = this.allTags();
    if (!tags) return [];
    const search = this.searchInput().toLowerCase().trim();
    if (!search) return tags;
    return tags.filter(
      (t) => t.name.toLowerCase().includes(search) || t.path.toLowerCase().includes(search),
    );
  });

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onTagSelect(id: string): void {
    const tags = this.allTags();
    const tag = tags?.find((t) => t.id === id) ?? null;
    if (tag) {
      this.selectedTag.set(tag);
      this.sheetState.set('open');
    }
  }

  onSheetSaved(): void {
    this.sheetState.set('closed');
    this.onRefresh();
  }

  onSheetClosed(): void {
    this.sheetState.set('closed');
    this.selectedTag.set(null);
  }

  onRefresh(): void {
    this.refreshTrigger.update((v) => v + 1);
  }

  clearSelection(): void {
    this.selectedTagIds.set(new Set());
  }

  onBulkDelete(ctx: { close: () => void }): void {
    const ids = Array.from(this.selectedTagIds());
    if (ids.length === 0) return;

    this.deleting.set(true);

    bulkDeleteTags(this.http, this.apiConfig.rootUrl, {
      body: { ids },
    }).subscribe({
      next: (response) => {
        this.deleting.set(false);
        ctx.close();
        const count = +response.body.deletedCount;
        toast.success(`${count} tag${count > 1 ? 's' : ''} deleted.`);
        this.clearSelection();
        this.onRefresh();
      },
      error: () => {
        this.deleting.set(false);
        toast.error('Failed to delete tags. Please try again.');
      },
    });
  }
}
