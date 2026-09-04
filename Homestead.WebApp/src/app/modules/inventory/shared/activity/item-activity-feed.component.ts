import {
  Component,
  ChangeDetectionStrategy,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
  viewChild,
  DestroyRef,
  ElementRef,
  LOCALE_ID,
} from '@angular/core';
import { formatDate } from '@angular/common';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, of, catchError, debounceTime, distinctUntilChanged } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideChevronLeft,
  lucideChevronRight,
  lucideEllipsisVertical,
  lucideHistory,
  lucidePencil,
  lucideStickyNote,
  lucideTrash2,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmToggleGroupImports } from '@/shared/components/spartan/toggle-group';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { HlmAutocompleteImports } from '@/shared/components/spartan/autocomplete';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { formatRelativeAge } from '@/shared/utils/relative-age';
import { formatNoteAge } from './note-age';
import { needsClamp } from './note-body';
import { matchSubjects } from './subject-suggestions';
import {
  ItemNoteComposerComponent,
  type NotePayload,
  type NoteSeed,
} from './item-note-composer.component';
import type { GetItemActivityResponse } from '@/api/generated/models/get-item-activity-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

const PAGE_SIZE = 20;
type ActivityView = 'all' | 'notes';

/**
 * The per-item activity feed: one chronological stream interleaving the item's audit-log projection
 * with user-authored, back-dateable notes. Rendered last on both inventory detail pages (durable and
 * consumable), so it is shared here rather than owned by either.
 *
 * Two reads back it — the paged feed and the item's distinct note subjects (the autocomplete source)
 * — each keyed on a plain object so a change to *any* input re-fetches. **Every mutation reloads
 * rather than splicing**: create returns to page 1, edit and delete re-read the current page (falling
 * back a page when a delete empties it). The `toSignal(toObservable(key) → switchMap)` shape keeps
 * the rows already on screen until the replacement lands, so a reload never flashes a spinner.
 *
 * The **default view** is "Notes when the item has any note, else All". With no stored preference
 * the first request is `notesOnly`; a zero-count response flips this item (only) to All and
 * re-fetches. A user tap persists via `UserPreferencesService` and wins from then on. A non-empty
 * filter forces Notes, since the filter matches note text only.
 */
@Component({
  selector: 'app-item-activity-feed',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmToggleGroupImports,
    HlmAlertDialogImports,
    HlmDropdownMenuImports,
    HlmAutocompleteImports,
    ItemNoteComposerComponent,
  ],
  providers: [provideIcons({
    lucideChevronLeft,
    lucideChevronRight,
    lucideEllipsisVertical,
    lucideHistory,
    lucidePencil,
    lucideStickyNote,
    lucideTrash2,
  })],
  template: `
    <section class="space-y-2">
      <!-- flex-wrap + basis-full below md: the controls drop to their own line rather than
           squeezing the title, so no width branch is needed. -->
      <div class="flex flex-wrap items-center gap-2">
        <h2 class="text-sm font-medium text-muted-foreground">Activity</h2>
        <div class="ml-auto flex flex-wrap items-center gap-2 max-md:ml-0 max-md:basis-full">
          <hlm-toggle-group
            type="single"
            nullable="false"
            variant="outline"
            aria-label="Activity view"
            [value]="view()"
            (valueChange)="onViewChange($event)"
          >
            <!-- The filter matches note text only, so All is unreachable while one is active. -->
            <button hlmToggleGroupItem value="all" [disabled]="filtering()">All</button>
            <button hlmToggleGroupItem value="notes">Notes</button>
          </hlm-toggle-group>

          <label class="sr-only" [attr.for]="filterInputId">Filter notes</label>
          <hlm-autocomplete
            class="max-md:flex-1 md:w-56"
            [search]="filterInput()"
            (searchChange)="onFilterInput($event)"
            (valueChange)="onFilterPicked($event)"
            [itemToString]="identity"
          >
            <hlm-autocomplete-input [inputId]="filterInputId" placeholder="Filter notes…" showClear />
            <hlm-autocomplete-content *hlmAutocompletePortal>
              @if (filterSuggestions().length) {
                <div hlmAutocompleteList>
                  @for (s of filterSuggestions(); track s) {
                    <hlm-autocomplete-item [value]="s">{{ s }}</hlm-autocomplete-item>
                  }
                </div>
              }
            </hlm-autocomplete-content>
          </hlm-autocomplete>
        </div>
      </div>

      <div class="rounded-md border">
        <div class="border-b px-4 py-3">
          <app-item-note-composer
            #headerComposer
            [subjects]="subjects()"
            [disabled]="editSeed() !== null"
            [busy]="submitting()"
            (submitted)="onCreate($event)"
          />
        </div>

        <div class="px-4 py-1">
          @if (feedError()) {
            <p class="py-6 text-center text-sm text-muted-foreground">
              Couldn't load activity.
              <button type="button" class="underline underline-offset-4 hover:text-foreground" (click)="retry()">Retry</button>
            </p>
          } @else if (!loaded()) {
            <div class="flex justify-center py-8"><hlm-spinner /></div>
          } @else if (rows().length === 0) {
            <p class="py-6 text-center text-sm text-muted-foreground">{{ emptyCopy() }}</p>
          } @else {
            @for (row of rows(); track row.id) {
              @let editing = editSeed();
              @if (row.type === 'note') {
                @if (editing && editing.id === row.id) {
                  <div class="py-2" #editRow>
                    <app-item-note-composer
                      mode="edit"
                      [subjects]="subjects()"
                      [initial]="editing.seed"
                      [busy]="saving()"
                      (submitted)="onSaveEdit(row, $event)"
                      (cancelled)="cancelEdit()"
                    />
                  </div>
                } @else {
                  <div class="flex gap-2.5 py-2">
                    <div class="flex size-6 shrink-0 items-center justify-center rounded-full bg-muted">
                      <ng-icon hlm size="xs" name="lucideStickyNote" />
                    </div>
                    <div class="min-w-0 flex-1">
                      <div class="flex flex-wrap items-baseline gap-x-2">
                        <span class="text-sm font-medium">{{ row.userDisplayName || row.userEmail || 'Someone' }}</span>
                        <span class="text-xs text-muted-foreground">{{ noteAge(row) }}</span>
                        @if (row.isEdited) {
                          <!-- Any contributor can rewrite anyone's note, so the row must not silently
                               keep the original author's name over changed text. -->
                          <span class="text-xs text-muted-foreground" [title]="editedTitle(row)">&middot; edited</span>
                        }
                      </div>
                      @if (row.subject) {
                        <p class="mt-0.5 text-sm font-medium break-words">{{ row.subject }}</p>
                      }
                      @if (row.body) {
                        <p
                          class="mt-0.5 text-sm break-words whitespace-pre-wrap"
                          [class.line-clamp-3]="clamped(row)"
                        >{{ row.body }}</p>
                        @if (needsClamp(row.body)) {
                          <button
                            type="button"
                            class="text-xs text-muted-foreground underline underline-offset-4 hover:text-foreground"
                            (click)="toggleExpanded(row.id)"
                          >{{ clamped(row) ? 'Show more' : 'Show less' }}</button>
                        }
                      }
                    </div>
                    <button
                      hlmBtn
                      variant="ghost"
                      size="icon-xs"
                      class="ml-auto shrink-0 self-start"
                      [hlmDropdownMenuTrigger]="rowMenu"
                      aria-label="Note actions"
                    >
                      <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
                    </button>
                    <ng-template #rowMenu>
                      <hlm-dropdown-menu class="w-36">
                        <hlm-dropdown-menu-group>
                          <button hlmDropdownMenuItem (triggered)="startEdit(row)">
                            <ng-icon hlm name="lucidePencil" size="sm" />
                            Edit
                          </button>
                          <button hlmDropdownMenuItem variant="destructive" (triggered)="onDeleteRequest(row)">
                            <ng-icon hlm name="lucideTrash2" size="sm" />
                            Delete
                          </button>
                        </hlm-dropdown-menu-group>
                      </hlm-dropdown-menu>
                    </ng-template>
                  </div>
                }
              } @else {
                <!-- Audit rows read as "recorded" events, so they keep the quiet {summary} · {age}
                     form rather than implying an occurrence date. -->
                <div class="flex items-center gap-2.5 py-1.5">
                  <div class="flex size-6 shrink-0 items-center justify-center">
                    <ng-icon hlm size="xs" name="lucideHistory" class="text-muted-foreground" />
                  </div>
                  <p class="text-xs text-muted-foreground">
                    {{ row.summary }}
                    <span class="mx-0.5">&middot;</span>
                    {{ relativeAge(row.timestamp) }}
                  </p>
                </div>
              }
            }
          }
        </div>
      </div>

      @if (totalPages() > 1) {
        <div class="flex items-center justify-end gap-2">
          <span class="text-xs text-muted-foreground">Page {{ page() }} of {{ totalPages() }}</span>
          <button hlmBtn variant="outline" size="icon-sm" [disabled]="page() <= 1" (click)="prevPage()" aria-label="Previous page">
            <ng-icon hlm name="lucideChevronLeft" size="sm" />
          </button>
          <button hlmBtn variant="outline" size="icon-sm" [disabled]="page() >= totalPages()" (click)="nextPage()" aria-label="Next page">
            <ng-icon hlm name="lucideChevronRight" size="sm" />
          </button>
        </div>
      }
    </section>

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete this note?</h2>
          <p hlmAlertDialogDescription>This permanently removes the note. There is no undo.</p>
        </div>
        @if (deleteError()) {
          <p class="text-sm text-destructive">{{ deleteError() }}</p>
        }
        <div hlmAlertDialogFooter>
          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
          <button hlmAlertDialogAction variant="destructive" [disabled]="deleting()" (click)="confirmDelete(ctx)">
            {{ deleting() ? 'Deleting...' : 'Delete' }}
          </button>
        </div>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class ItemActivityFeedComponent {
  private static _id = 0;
  protected readonly filterInputId = `activity-filter-${ItemActivityFeedComponent._id++}`;

  private api = inject(InventoryItemApiService);
  private prefs = inject(UserPreferencesService);
  private errorHandler = inject(ErrorHandlerService);
  private injector = inject(Injector);
  private destroyRef = inject(DestroyRef);
  private locale = inject(LOCALE_ID);

  itemId = input.required<string>();
  /** The host page's refresh tick, so an edit-sheet save or status change refreshes the audit rows. */
  refreshTick = input(0);

  protected readonly identity = (v: string): string => v;
  protected readonly relativeAge = formatRelativeAge;
  protected readonly needsClamp = needsClamp;

  // --- View selection -------------------------------------------------------------------------
  /** Set to 'all' when the default probe finds this item has no notes. Per-item, never persisted. */
  private probedDefault = seededLinkedSignal<string, 'all' | null>(this.itemId, () => null);

  protected filterInput = seededLinkedSignal<string, string>(this.itemId, () => '');
  private filter = toSignal(
    toObservable(this.filterInput).pipe(
      map((v) => v.trim()),
      debounceTime(300),
      distinctUntilChanged(),
    ),
    { initialValue: '' },
  );
  // Debounced, not live: `view()` keys the fetch, so a live read would fire an unfiltered
  // notes-only request on the first keystroke and the real one 300 ms later.
  protected filtering = computed(() => this.filter().length > 0);

  protected view = computed<ActivityView>(() => {
    if (this.filtering()) return 'notes';
    // A tap writes the persisted preference directly, so that one signal is the explicit choice.
    return this.prefs.inventoryActivityView() ?? this.probedDefault() ?? 'notes';
  });

  // --- The paged feed -------------------------------------------------------------------------
  protected page = seededLinkedSignal<string, number>(this.itemId, () => 1);
  private localTick = signal(0);
  protected feedError = signal(false);

  private feedKey = computed(() => ({
    id: this.itemId(),
    page: this.page(),
    notesOnly: this.view() === 'notes',
    filter: this.filter(),
    tick: this.refreshTick() + this.localTick(),
  }));

  private feedPage = toSignal(
    toObservable(this.feedKey).pipe(
      switchMap(({ id, page, notesOnly, filter }) => {
        this.feedError.set(false);
        if (!id) return of(null);
        return this.api
          .getItemActivity({ id, Page: page, PageSize: PAGE_SIZE, notesOnly, filter: filter || undefined })
          .pipe(
            map((res) => {
              this.probeDefaultView(notesOnly, filter, res.totalCount);
              return res;
            }),
            catchError(() => {
              this.feedError.set(true);
              return of(null);
            }),
          );
      }),
    ),
    { initialValue: null },
  );

  protected rows = computed<GetItemActivityResponse[]>(() => this.feedPage()?.items ?? []);
  protected totalPages = computed(() => this.feedPage()?.totalPages ?? 1);
  /** Distinguishes the first in-flight fetch from a resolved-but-empty page (no empty-state flash). */
  protected loaded = computed(() => this.feedPage() !== null);

  protected emptyCopy = computed(() => {
    if (this.filtering()) return 'No notes match.';
    return this.view() === 'notes' ? 'No notes yet.' : 'No activity yet.';
  });

  // --- Subject suggestions --------------------------------------------------------------------
  private subjectsTick = signal(0);
  private subjectsKey = computed(() => ({ id: this.itemId(), tick: this.subjectsTick() }));

  protected subjects = toSignal(
    toObservable(this.subjectsKey).pipe(
      switchMap(({ id }) =>
        id ? this.api.getItemNoteSubjects({ id }).pipe(catchError(() => of([] as string[]))) : of([] as string[]),
      ),
    ),
    { initialValue: [] as string[] },
  );

  protected filterSuggestions = computed(() => matchSubjects(this.subjects(), this.filterInput()));

  // --- Row state ------------------------------------------------------------------------------
  /** The note being edited plus its seed, captured once so a feed reload underneath can't re-seed. */
  protected editSeed = seededLinkedSignal<string, { id: string; seed: NoteSeed } | null>(this.itemId, () => null);
  private expanded = seededLinkedSignal<string, ReadonlySet<string>>(this.itemId, () => new Set<string>());
  protected submitting = signal(false);
  protected saving = signal(false);

  protected pendingDelete = signal<GetItemActivityResponse | null>(null);
  protected deleteState = seededLinkedSignal<string, BrnDialogState>(this.itemId, () => 'closed');
  protected deleting = signal(false);
  protected deleteError = signal('');

  private headerComposer = viewChild<ItemNoteComposerComponent>('headerComposer');
  private editRow = viewChild<ElementRef<HTMLElement>>('editRow');

  // --- Display helpers ------------------------------------------------------------------------
  protected noteAge(row: GetItemActivityResponse): string {
    return formatNoteAge(row.occurredAt ?? row.timestamp, this.locale);
  }

  protected editedTitle(row: GetItemActivityResponse): string {
    const who = row.updatedByDisplayName || row.updatedByEmail || 'someone';
    const when = row.updatedAt ? formatDate(row.updatedAt, 'medium', this.locale) : '';
    return when ? `Edited by ${who} · ${when}` : `Edited by ${who}`;
  }

  protected clamped(row: GetItemActivityResponse): boolean {
    return needsClamp(row.body) && !this.expanded().has(row.id);
  }

  protected toggleExpanded(id: string): void {
    const next = new Set(this.expanded());
    if (!next.delete(id)) next.add(id);
    this.expanded.set(next);
  }

  // --- Handlers -------------------------------------------------------------------------------
  protected onViewChange(value: unknown): void {
    if (value !== 'all' && value !== 'notes') return;
    this.leaveEdit();
    this.prefs.inventoryActivityView.set(value);
    this.page.set(1);
  }

  protected onFilterInput(value: string): void {
    this.leaveEdit();
    this.filterInput.set(value);
    this.page.set(1);
  }

  protected onFilterPicked(value: unknown): void {
    if (typeof value === 'string') this.onFilterInput(value);
  }

  protected prevPage(): void {
    this.leaveEdit();
    this.page.update((p) => Math.max(1, p - 1));
  }

  protected nextPage(): void {
    this.leaveEdit();
    this.page.update((p) => Math.min(this.totalPages(), p + 1));
  }

  /**
   * Drop an open row edit before the rows change under it. The inline editor — and with it the only
   * Cancel button — lives inside the row loop, so if the edited note left the page the header composer
   * would stay disabled with nothing on screen to re-enable it.
   */
  private leaveEdit(): void {
    this.editSeed.set(null);
  }

  protected retry(): void {
    this.feedError.set(false);
    this.localTick.update((v) => v + 1);
  }

  protected onCreate(payload: NotePayload): void {
    this.submitting.set(true);
    this.api
      .createItemNote({ id: this.itemId(), body: payload })
      .pipe(takeUntilDestroyed(this.destroyRef), handleMutationError(this.errorHandler, this.submitting, 'Failed to add note.'))
      .subscribe(() => {
        this.submitting.set(false);
        // Clear the text but keep the date — back-filling a run of entries is one date change.
        this.headerComposer()?.clearText();
        this.page.set(1);
        this.reload();
      });
  }

  protected startEdit(row: GetItemActivityResponse): void {
    if (this.submitting()) return;
    this.editSeed.set({
      id: row.id,
      seed: { subject: row.subject ?? null, body: row.body ?? '', occurredAt: row.occurredAt ?? row.timestamp },
    });
    afterNextRender(() => this.editRow()?.nativeElement.scrollIntoView({ block: 'nearest' }), {
      injector: this.injector,
    });
  }

  protected cancelEdit(): void {
    this.editSeed.set(null);
  }

  protected onSaveEdit(row: GetItemActivityResponse, payload: NotePayload): void {
    this.saving.set(true);
    this.api
      .updateItemNote({
        id: this.itemId(),
        noteId: row.id,
        body: {
          subject: payload.subject,
          body: payload.body,
          occurredAt: payload.occurredAt ?? row.occurredAt ?? row.timestamp,
        },
      })
      .pipe(takeUntilDestroyed(this.destroyRef), handleMutationError(this.errorHandler, this.saving, 'Failed to save note.'))
      .subscribe(() => {
        this.saving.set(false);
        this.editSeed.set(null);
        this.reload();
      });
  }

  protected onDeleteRequest(row: GetItemActivityResponse): void {
    this.pendingDelete.set(row);
    this.deleteError.set('');
    this.deleteState.set('open');
  }

  protected confirmDelete(ctx: { close: () => void }): void {
    const row = this.pendingDelete();
    if (!row) return;
    this.deleting.set(true);
    this.deleteError.set('');
    this.api
      .deleteItemNote({ id: this.itemId(), noteId: row.id })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.deleting.set(false);
          ctx.close();
          // Deleting the only row of a later page would otherwise strand the reader on an empty page.
          if (this.rows().length === 1 && this.page() > 1) this.page.update((p) => p - 1);
          this.reload();
          toast.success('Note deleted.');
        },
        // Inline, not a toast: the open dialog overlay would obscure a toast, and the rejection
        // reason is what the user needs to read right here.
        error: (err: unknown) => {
          this.deleting.set(false);
          this.deleteError.set(this.errorHandler.getErrorMessage(err, 'Failed to delete note.'));
        },
      });
  }

  /** Every mutation re-reads rather than splicing; subjects follow so the autocomplete stays current. */
  private reload(): void {
    this.localTick.update((v) => v + 1);
    this.subjectsTick.update((v) => v + 1);
  }

  /**
   * The default-view probe. Only when nothing else has spoken — no persisted preference (a tap writes
   * one), no filter — and the notes-only page came back empty do we flip this item to All.
   * Writing `probedDefault` re-keys the feed, so the All page is fetched immediately after.
   */
  private probeDefaultView(notesOnly: boolean, filter: string, totalCount: number): void {
    if (!notesOnly || filter || totalCount !== 0) return;
    if (this.prefs.inventoryActivityView() !== null || this.probedDefault() !== null) return;
    this.probedDefault.set('all');
  }
}
