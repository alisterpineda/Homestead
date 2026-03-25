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
import { switchMap, debounceTime, of, map, tap, catchError } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUsers, lucideCheck, lucideMinus, lucideX } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { PartyApiService } from '@/api/services/party-api.service';
import { DocumentApiService } from '@/api/services/document-api.service';
import { PartyBadgeComponent } from '../../parties/party-badge/party-badge.component';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';
import type { GetPartiesResponse } from '@/api/generated/models/get-parties-response';
import { toast } from 'ngx-sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { type PartyCheckboxItem, nextState, isChanged, buildConfirmationMessage, CREATE_OPTION_ID } from '../../bulk-edit-shared';

@Component({
  selector: 'app-bulk-edit-parties-popover',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmPopoverImports,
    HlmSeparatorImports,
    HlmInput,
    HlmButton,
    HlmAlertDialogImports,
    PartyBadgeComponent,
  ],
  providers: [provideIcons({ lucideUsers, lucideCheck, lucideMinus, lucideX })],
  host: {
    '(document:mousedown)': 'onClickOutside($event)',
  },
  template: `
    <div hlmPopover [state]="popoverState()" (stateChanged)="onPopoverStateChanged($event)">
      <button hlmPopoverTrigger hlmBtn variant="outline" size="sm" [disabled]="disabled()">
        <ng-icon hlm name="lucideUsers" size="sm" />
        Parties
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-[280px] p-0" align="start">
        <div class="p-2 relative" #partyContainer>
          <input
            hlmInput
            #partySearchEl
            class="h-8 w-full"
            placeholder="Search parties to add..."
            [value]="partySearchText()"
            (input)="onSearchInput($event)"
            (focus)="onSearchFocus()"
            (keydown)="onKeyDown($event)"
          />
          @if (showSuggestions() && suggestionOptions().length > 0) {
            <div class="absolute left-2 right-2 z-50 mt-1 rounded-md border border-border bg-popover shadow-lg max-h-48 overflow-y-auto">
              @for (option of suggestionOptions(); track option.id; let i = $index) {
                <button
                  type="button"
                  (click)="selectOption(option)"
                  [class]="
                    'w-full px-3 py-2 text-left text-sm transition-colors cursor-pointer truncate ' +
                    (i === selectedIndex()
                      ? 'bg-accent text-accent-foreground'
                      : 'text-foreground hover:bg-accent') +
                    (i === 0 ? ' rounded-t-md' : '') +
                    (i === suggestionOptions().length - 1 ? ' rounded-b-md' : '')
                  "
                >
                  @if (option.id === createOptionId) {
                    <span class="italic">Create "{{ option.name }}"</span>
                  } @else {
                    {{ option.name }}
                  }
                </button>
              }
            </div>
          }
        </div>
        <span hlmSeparator></span>
        <div class="max-h-[200px] overflow-y-auto p-1">
          @if (items().length === 0) {
            <p class="py-4 text-center text-sm text-muted-foreground">No parties on selected documents.</p>
          } @else {
            @for (item of items(); track item.id) {
              <button
                type="button"
                class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
                (click)="toggleItem(item.id)"
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
                <app-party-badge [name]="item.name" />
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
                  <h2 hlmAlertDialogTitle>Apply party changes?</h2>
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
  `,
})
export class BulkEditPartiesPopoverComponent {
  protected readonly createOptionId = CREATE_OPTION_ID;

  selectedIds = input.required<Set<string>>();
  documents = input.required<GetDocumentsResponse[]>();
  disabled = input(false);
  applied = output();

  items = signal<PartyCheckboxItem[]>([]);
  applying = signal(false);
  popoverState = signal<'open' | 'closed'>('closed');

  // Search
  partySearchText = signal('');
  showSuggestions = signal(false);
  selectedIndex = signal(0);
  creatingParty = signal(false);
  loading = signal(false);

  private partyContainer = viewChild<ElementRef<HTMLDivElement>>('partyContainer');
  private partySearchEl = viewChild<ElementRef<HTMLInputElement>>('partySearchEl');
  private partyApi = inject(PartyApiService);
  private documentApi = inject(DocumentApiService);
  private errorHandler = inject(ErrorHandlerService);

  // Party autocomplete
  private suggestions = toSignal(
    toObservable(this.partySearchText).pipe(
      debounceTime(200),
      switchMap((text) => {
        if (!text) {
          this.loading.set(false);
          return of([]);
        }
        return this.partyApi.getParties({
          Search: text,
          Page: 1,
          PageSize: 10,
        }).pipe(
          map((r) => r.items as GetPartiesResponse[]),
          tap(() => this.loading.set(false)),
          catchError(() => {
            this.loading.set(false);
            return of([]);
          }),
        );
      }),
    ),
    { initialValue: [] as GetPartiesResponse[] },
  );

  suggestionOptions = computed(() => {
    const existingIds = new Set(this.items().map((p) => p.id));
    const filtered = (this.suggestions() ?? []).filter((s) => !existingIds.has(s.id));
    const options: { id: string; name: string }[] = filtered.map((s) => ({ id: s.id, name: s.name }));
    const text = this.partySearchText().trim();
    const lowerText = text.toLowerCase();
    if (text && !this.loading() && !filtered.some((s) => s.name.toLowerCase() === lowerText)) {
      options.push({ id: CREATE_OPTION_ID, name: text });
    }
    return options;
  });

  private changeSummary = computed(() => {
    let hasChanges = false;
    let addCount = 0;
    let removeCount = 0;
    for (const p of this.items()) {
      if (isChanged(p.state, p.initial)) hasChanges = true;
      if (p.state === 'checked' && p.initial !== 'all') addCount++;
      if (p.state === 'remove') removeCount++;
    }
    return { hasChanges, addCount, removeCount };
  });

  hasChanges = computed(() => this.changeSummary().hasChanges);
  addCount = computed(() => this.changeSummary().addCount);
  removeCount = computed(() => this.changeSummary().removeCount);

  confirmationMessage = computed(() =>
    buildConfirmationMessage(this.addCount(), this.removeCount(), this.selectedIds().size, 'party', 'parties'),
  );

  constructor() {
    effect(() => {
      this.partySearchText();
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

    const partyCounts = new Map<string, { count: number; name: string }>();
    for (const doc of docs) {
      for (const party of doc.parties) {
        const existing = partyCounts.get(party.id);
        if (existing) {
          existing.count++;
        } else {
          partyCounts.set(party.id, { count: 1, name: party.name });
        }
      }
    }

    const items: PartyCheckboxItem[] = [];
    for (const [id, entry] of partyCounts) {
      if (entry.count === selectedCount) {
        items.push({ id, name: entry.name, initial: 'all', state: 'checked' });
      } else {
        items.push({ id, name: entry.name, initial: 'some', state: 'mixed' });
      }
    }
    this.items.set(items);

    this.partySearchText.set('');
    this.applying.set(false);
    this.showSuggestions.set(false);
    this.loading.set(false);
  }

  toggleItem(id: string): void {
    this.items.update((items) =>
      items.map((item) =>
        item.id === id ? { ...item, state: nextState(item.state, item.initial) } : item,
      ),
    );
  }

  confirmApply(ctx: { close: () => void }): void {
    if (!this.hasChanges()) return;

    const partyIdsToAdd = this.items()
      .filter((p) => p.state === 'checked' && p.initial !== 'all')
      .map((p) => p.id);
    const partyIdsToRemove = this.items()
      .filter((p) => p.state === 'remove')
      .map((p) => p.id);

    const ids = Array.from(this.selectedIds());
    this.applying.set(true);

    this.documentApi.bulkUpdateDocuments({
      body: {
        ids,
        tagPaths: null,
        removeTagPaths: null,
        partyIds: partyIdsToAdd.length > 0 ? partyIdsToAdd : null,
        removePartyIds: partyIdsToRemove.length > 0 ? partyIdsToRemove : null,
      },
    }).pipe(
      handleMutationError(this.errorHandler, this.applying, 'Failed to update documents. Please try again.'),
    ).subscribe({
      next: (response) => {
        this.applying.set(false);
        const count = +response.updatedCount;
        toast.success(`Updated ${count} document${count > 1 ? 's' : ''}.`);
        ctx.close();
        this.popoverState.set('closed');
        this.applied.emit();
      },
    });
  }

  // --- Search ---
  onSearchInput(event: Event): void {
    const val = (event.target as HTMLInputElement).value;
    this.partySearchText.set(val);
    this.loading.set(!!val.trim());
    this.showSuggestions.set(true);
  }

  onSearchFocus(): void {
    if (this.partySearchText()) {
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

  selectOption(option: { id: string; name: string }): void {
    if (option.id === CREATE_OPTION_ID) {
      this.createAndAddParty(option.name);
    } else {
      this.addFromSuggestion(option);
    }
  }

  addFromSuggestion(party: { id: string; name: string }): void {
    const existingIndex = this.items().findIndex((p) => p.id === party.id);
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
          id: party.id,
          name: party.name,
          initial: 'new' as const,
          state: 'checked' as const,
        },
      ]);
    }
    this.partySearchText.set('');
    this.showSuggestions.set(false);
    this.partySearchEl()?.nativeElement.focus();
  }

  private createAndAddParty(name: string): void {
    if (this.creatingParty()) return;
    this.creatingParty.set(true);
    this.partyApi.createParty({
      body: { name },
    }).subscribe({
      next: (r) => {
        this.creatingParty.set(false);
        this.addFromSuggestion({ id: r.id, name: r.name });
      },
      error: () => {
        this.creatingParty.set(false);
      },
    });
  }

  // --- Click outside ---
  onClickOutside(event: MouseEvent): void {
    if (!this.showSuggestions()) return;
    const target = event.target as Node;
    const el = this.partyContainer()?.nativeElement;
    if (el && !el.contains(target)) {
      this.showSuggestions.set(false);
    }
  }
}
