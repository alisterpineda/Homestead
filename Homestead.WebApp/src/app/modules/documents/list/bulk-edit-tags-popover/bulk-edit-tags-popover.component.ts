import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
  viewChild,
  ElementRef,
  effect,
  untracked,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, debounceTime, of, map, tap, catchError, firstValueFrom } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTags, lucideCheck, lucideMinus, lucideX } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { TagApiService } from '@/api/services/tag-api.service';
import { DocumentApiService } from '@/api/services/document-api.service';
import { TagBadgeComponent } from '../../tags/tag-badge/tag-badge.component';
import { CreateTagDialogComponent } from '../../tags/create-tag-dialog/create-tag-dialog.component';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';
import type { GetTagsResponse } from '@/api/generated/models/get-tags-response';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { type TagCheckboxItem, nextState, isChanged, buildConfirmationMessage } from '../../bulk-edit-shared';

@Component({
  selector: 'app-bulk-edit-tags-popover',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmPopoverImports,
    HlmSeparatorImports,
    HlmInput,
    HlmButton,
    HlmAlertDialogImports,
    TagBadgeComponent,
    CreateTagDialogComponent,
  ],
  providers: [provideIcons({ lucideTags, lucideCheck, lucideMinus, lucideX })],
  host: {
    '(document:mousedown)': 'onClickOutside($event)',
  },
  template: `
    <div hlmPopover [state]="popoverState()" [closeOnOutsidePointerEvents]="!dialogOpen()" (stateChanged)="onPopoverStateChanged($event)">
      <button hlmPopoverTrigger hlmBtn variant="outline" size="sm" [disabled]="disabled()">
        <ng-icon hlm name="lucideTags" size="sm" />
        Tags
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-[280px] p-0" align="start">
        <div class="p-2 relative" #tagContainer>
          <input
            hlmInput
            #tagSearchEl
            class="h-8 w-full"
            placeholder="Search tags to add..."
            [value]="tagSearchText()"
            (input)="onSearchInput($event)"
            (focus)="onSearchFocus()"
            (keydown)="onKeyDown($event)"
          />
          @if (showSuggestions() && suggestionOptions().length > 0) {
            <div class="absolute left-2 right-2 z-50 mt-1 rounded-md border border-border bg-popover shadow-lg max-h-48 overflow-y-auto">
              @for (option of suggestionOptions(); track option.path; let i = $index) {
                <button
                  type="button"
                  (click)="selectOption(option)"
                  [class]="
                    'w-full px-3 py-2 text-left text-sm transition-colors cursor-pointer flex items-center gap-2 ' +
                    (i === selectedIndex()
                      ? 'bg-accent text-accent-foreground'
                      : 'text-foreground hover:bg-accent') +
                    (i === 0 ? ' rounded-t-md' : '') +
                    (i === suggestionOptions().length - 1 ? ' rounded-b-md' : '')
                  "
                >
                  @if (option.type === 'create') {
                    <span class="italic truncate min-w-0">Create "{{ option.path }}"</span>
                  } @else {
                    @if (option.effectiveColor) {
                      <span class="h-3 w-3 shrink-0 rounded-full" [style.background-color]="option.effectiveColor"></span>
                    }
                    <span class="truncate min-w-0">{{ option.path }}</span>
                  }
                </button>
              }
            </div>
          }
        </div>
        <span hlmSeparator></span>
        <div class="max-h-[200px] overflow-y-auto p-1">
          @if (items().length === 0) {
            <p class="py-4 text-center text-sm text-muted-foreground">No tags on selected documents.</p>
          } @else {
            @for (item of items(); track item.path) {
              <button
                type="button"
                class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
                (click)="toggleItem(item.path)"
              >
                @switch (item.state) {
                  @case ('none') {
                    <span class="size-4 shrink-0 rounded-sm border border-primary" aria-label="Not applied"></span>
                  }
                  @case ('checked') {
                    <span class="size-4 shrink-0 rounded-sm bg-primary flex items-center justify-center" aria-label="Applied">
                      <ng-icon name="lucideCheck" class="text-primary-foreground !size-3" />
                    </span>
                  }
                  @case ('mixed') {
                    <span class="size-4 shrink-0 rounded-sm border border-primary flex items-center justify-center" aria-label="Partially applied">
                      <ng-icon name="lucideMinus" class="text-foreground !size-3" />
                    </span>
                  }
                  @case ('remove') {
                    <span class="size-4 shrink-0 rounded-sm border border-primary flex items-center justify-center" aria-label="Remove">
                      <ng-icon name="lucideX" class="text-foreground !size-3" />
                    </span>
                  }
                }
                <app-tag-badge [path]="item.path" [color]="item.color" />
              </button>
            }
          }
        </div>
        @if (items().length > 0) {
          <span hlmSeparator></span>
          <div class="flex items-center justify-end gap-2 p-2">
            @if (addCount() > 0 || removeCount() > 0) {
              <span class="mr-auto text-xs text-muted-foreground">
                @if (addCount() > 0) { {{ addCount() }} to add }
                @if (addCount() > 0 && removeCount() > 0) { · }
                @if (removeCount() > 0) { {{ removeCount() }} to remove }
              </span>
            }
            <hlm-alert-dialog>
              <button hlmAlertDialogTrigger hlmBtn size="sm" [disabled]="!hasChanges() || applying()">
                {{ applying() ? 'Applying...' : 'Apply' }}
              </button>
              <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
                <div hlmAlertDialogHeader>
                  <h2 hlmAlertDialogTitle>Apply tag changes?</h2>
                  <p hlmAlertDialogDescription>{{ confirmationMessage() }}</p>
                </div>
                <div hlmAlertDialogFooter>
                  <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
                  <button hlmAlertDialogAction (click)="confirmApply(ctx)" [disabled]="applying()">
                    {{ applying() ? 'Applying...' : 'Confirm' }}
                  </button>
                </div>
              </hlm-alert-dialog-content>
            </hlm-alert-dialog>
          </div>
        }
      </div>
    </div>

    <app-create-tag-dialog
      #createTagDialog
      [initialPath]="tagSearchText()"
      [hideTrigger]="true"
      (created)="onTagCreated($event)"
      (closed)="onDialogClosed()"
    />
  `,
})
export class BulkEditTagsPopoverComponent {
  selectedIds = input.required<Set<string>>();
  documents = input.required<GetDocumentsResponse[]>();
  disabled = input(false);
  applied = output();

  items = signal<TagCheckboxItem[]>([]);
  applying = signal(false);
  popoverState = signal<'open' | 'closed'>('closed');

  dialogOpen = signal(false);

  // Search
  tagSearchText = signal('');
  showSuggestions = signal(false);
  selectedIndex = signal(0);
  loading = signal(false);

  private tagContainer = viewChild<ElementRef<HTMLDivElement>>('tagContainer');
  private tagSearchEl = viewChild<ElementRef<HTMLInputElement>>('tagSearchEl');
  private createTagDialog = viewChild<CreateTagDialogComponent>('createTagDialog');
  private tagApi = inject(TagApiService);
  private documentApi = inject(DocumentApiService);
  private errorHandler = inject(ErrorHandlerService);

  // Tag autocomplete
  private suggestions = toSignal(
    toObservable(this.tagSearchText).pipe(
      debounceTime(200),
      switchMap((text) => {
        if (!text) {
          this.loading.set(false);
          return of([]);
        }
        return this.tagApi.getTags({
          Search: text,
          IncludeChildren: false,
          Page: 1,
          PageSize: 50,
        }).pipe(
          map((r) => r.items as GetTagsResponse[]),
          tap(() => this.loading.set(false)),
          catchError(() => {
            this.loading.set(false);
            return of([]);
          }),
        );
      }),
    ),
    { initialValue: [] as GetTagsResponse[] },
  );

  suggestionOptions = computed(() => {
    const existing = new Set(this.items().map((t) => t.path.toLowerCase()));
    const filtered = (this.suggestions() ?? []).filter((s) => !existing.has(s.path.toLowerCase()));
    const options: { type: 'existing' | 'create'; path: string; effectiveColor: string | null }[] =
      filtered.map((s) => ({ type: 'existing' as const, path: s.path, effectiveColor: s.effectiveColor ?? null }));

    const text = this.tagSearchText().trim();
    const lowerText = text.toLowerCase();
    if (
      text &&
      !this.loading() &&
      !filtered.some((s) => s.path.toLowerCase() === lowerText) &&
      !existing.has(lowerText)
    ) {
      options.push({ type: 'create', path: text, effectiveColor: null });
    }

    return options;
  });

  private changeSummary = computed(() => {
    let hasChanges = false;
    let addCount = 0;
    let removeCount = 0;
    for (const t of this.items()) {
      if (isChanged(t.state, t.initial)) hasChanges = true;
      if (t.state === 'checked' && t.initial !== 'all') addCount++;
      if (t.state === 'remove') removeCount++;
    }
    return { hasChanges, addCount, removeCount };
  });

  hasChanges = computed(() => this.changeSummary().hasChanges);
  addCount = computed(() => this.changeSummary().addCount);
  removeCount = computed(() => this.changeSummary().removeCount);

  confirmationMessage = computed(() =>
    buildConfirmationMessage(this.addCount(), this.removeCount(), this.selectedIds().size, 'tag', 'tags'),
  );

  constructor() {
    effect(() => {
      this.tagSearchText();
      if (untracked(this.selectedIndex) !== 0) this.selectedIndex.set(0);
    });
  }

  onPopoverStateChanged(state: 'open' | 'closed'): void {
    this.popoverState.set(state);
    if (state === 'open') {
      this.initializeState();
    }
  }

  initializeState(): void {
    const selected = this.selectedIds();
    const docs = this.documents().filter((d) => selected.has(d.id));
    const selectedCount = docs.length;

    const tagCounts = new Map<string, { count: number; color: string | null; path: string }>();
    for (const doc of docs) {
      for (const tag of doc.tags) {
        const key = tag.path.toLowerCase();
        const existing = tagCounts.get(key);
        if (existing) {
          existing.count++;
        } else {
          tagCounts.set(key, { count: 1, color: tag.effectiveColor ?? null, path: tag.path });
        }
      }
    }

    const items: TagCheckboxItem[] = [];
    for (const [, entry] of tagCounts) {
      if (entry.count === selectedCount) {
        items.push({ path: entry.path, color: entry.color, initial: 'all', state: 'checked' });
      } else {
        items.push({ path: entry.path, color: entry.color, initial: 'some', state: 'mixed' });
      }
    }
    this.items.set(items);

    this.tagSearchText.set('');
    this.applying.set(false);
    this.showSuggestions.set(false);
    this.loading.set(false);
  }

  toggleItem(path: string): void {
    this.items.update((items) =>
      items.map((item) =>
        item.path === path ? { ...item, state: nextState(item.state, item.initial) } : item,
      ),
    );
  }

  confirmApply(ctx: { close: () => void }): void {
    if (!this.hasChanges()) return;

    const tagPathsToAdd = this.items()
      .filter((t) => t.state === 'checked' && t.initial !== 'all')
      .map((t) => t.path);
    const tagPathsToRemove = this.items()
      .filter((t) => t.state === 'remove')
      .map((t) => t.path);

    const ids = Array.from(this.selectedIds());
    this.applying.set(true);

    const result$ = this.documentApi.bulkUpdateDocuments({
      body: {
        ids,
        tagPaths: tagPathsToAdd.length > 0 ? tagPathsToAdd : null,
        removeTagPaths: tagPathsToRemove.length > 0 ? tagPathsToRemove : null,
        partyIds: null,
        removePartyIds: null,
      },
    }).pipe(
      handleMutationError(this.errorHandler, this.applying, 'Failed to update documents. Please try again.'),
    );

    toast.promise(
      firstValueFrom(result$).then((response) => {
        this.applying.set(false);
        const count = +response.updatedCount;
        ctx.close();
        this.popoverState.set('closed');
        this.applied.emit();
        return `Updated ${count} document${count > 1 ? 's' : ''}.`;
      }),
      {
        loading: 'Updating documents...',
        success: (message) => message,
      },
    );
  }

  // --- Search ---
  onSearchInput(event: Event): void {
    const val = (event.target as HTMLInputElement).value;
    this.tagSearchText.set(val);
    this.loading.set(!!val.trim());
    this.showSuggestions.set(true);
  }

  onSearchFocus(): void {
    if (this.tagSearchText()) {
      this.showSuggestions.set(true);
    }
  }

  onKeyDown(event: KeyboardEvent): void {
    const options = this.suggestionOptions();
    if (event.key === 'Enter') {
      event.preventDefault();
      if (this.showSuggestions() && options.length > 0 && options[this.selectedIndex()]) {
        this.selectOption(options[this.selectedIndex()]);
      }
    } else if (event.key === 'ArrowDown' && this.showSuggestions()) {
      event.preventDefault();
      this.selectedIndex.update((i) => (i < options.length - 1 ? i + 1 : i));
    } else if (event.key === 'ArrowUp' && this.showSuggestions()) {
      event.preventDefault();
      this.selectedIndex.update((i) => (i > 0 ? i - 1 : 0));
    } else if (event.key === 'Escape') {
      this.showSuggestions.set(false);
    }
  }

  selectOption(option: { type: 'existing' | 'create'; path: string; effectiveColor: string | null }): void {
    if (option.type === 'create') {
      this.showSuggestions.set(false);
      this.dialogOpen.set(true);
      this.createTagDialog()?.open();
    } else {
      this.addFromSuggestion({ path: option.path, effectiveColor: option.effectiveColor });
    }
  }

  onTagCreated(event: { path: string; effectiveColor: string | null }): void {
    this.addFromSuggestion({ path: event.path, effectiveColor: event.effectiveColor });
  }

  onDialogClosed(): void {
    // Defer so dialogOpen stays true through the CDK overlay cleanup that follows
    // the dialog close — otherwise the popover's outside-pointer-event handler
    // fires in the same tick and closes the popover.
    setTimeout(() => this.dialogOpen.set(false));
  }

  addFromSuggestion(suggestion: Pick<GetTagsResponse, 'path' | 'effectiveColor'>): void {
    const existingIndex = this.items().findIndex(
      (t) => t.path.toLowerCase() === suggestion.path.toLowerCase(),
    );
    if (existingIndex >= 0) {
      this.items.update((items) =>
        items.map((item, i) =>
          i === existingIndex ? { ...item, state: 'checked' as const } : item,
        ),
      );
    } else {
      this.items.update((items) => [
        ...items,
        {
          path: suggestion.path,
          color: suggestion.effectiveColor ?? null,
          initial: 'new' as const,
          state: 'checked' as const,
        },
      ]);
    }
    this.tagSearchText.set('');
    this.showSuggestions.set(false);
    this.tagSearchEl()?.nativeElement.focus();
  }

  // --- Click outside ---
  onClickOutside(event: MouseEvent): void {
    if (!this.showSuggestions()) return;
    const target = event.target as Node;
    const el = this.tagContainer()?.nativeElement;
    if (el && !el.contains(target)) {
      this.showSuggestions.set(false);
    }
  }
}
