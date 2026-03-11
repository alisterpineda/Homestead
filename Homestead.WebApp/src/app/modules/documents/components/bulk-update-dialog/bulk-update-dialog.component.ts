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
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, debounceTime, of, map } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTags } from '@ng-icons/lucide';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmInput } from '@/shared/components/spartan/input';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { bulkUpdateDocuments } from '@/api/generated/fn/documents/bulk-update-documents';
import { getTags } from '@/api/generated/fn/documents/get-tags';
import { getParties } from '@/api/generated/fn/documents/get-parties';
import { createParty } from '@/api/generated/fn/documents/create-party';
import { TagBadgeComponent } from '../tag-badge/tag-badge.component';
import { PartyBadgeComponent } from '../party-badge/party-badge.component';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';
import type { GetTagsResponse } from '@/api/generated/models/get-tags-response';
import type { GetPartiesResponse } from '@/api/generated/models/get-parties-response';
import { toast } from 'ngx-sonner';

const CREATE_PARTY_ID = '__create__';

export interface TagCheckboxItem {
  path: string;
  color: string | null;
  initial: 'all' | 'some' | 'new';
  checked: boolean;
  indeterminate: boolean;
}

export interface PartyCheckboxItem {
  id: string;
  name: string;
  initial: 'all' | 'some' | 'new';
  checked: boolean;
  indeterminate: boolean;
}

@Component({
  selector: 'app-bulk-update-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmDialogImports,
    HlmButton,
    HlmIconImports,
    HlmLabel,
    HlmCheckboxImports,
    HlmInput,
    TagBadgeComponent,
    PartyBadgeComponent,
  ],
  providers: [provideIcons({ lucideTags })],
  host: {
    '(document:mousedown)': 'onClickOutside($event)',
  },
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn variant="outline" size="sm" hlmDialogTrigger>
        <ng-icon hlm name="lucideTags" size="sm" />
        Edit Tags / Parties
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Edit Tags &amp; Parties</h2>
          <p hlmDialogDescription>
            Manage tags and parties for {{ selectedIds().size }} selected document{{ selectedIds().size > 1 ? 's' : '' }}.
          </p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Tags</label>
            <div class="relative" #tagContainer>
              <input
                hlmInput
                #tagSearchEl
                [value]="tagSearchText()"
                (input)="onTagSearchInput($event)"
                (focus)="onTagSearchFocus()"
                (keydown)="onTagKeyDown($event)"
                placeholder="Search tags to add..."
                class="w-full"
              />
              @if (showTagSuggestions() && tagSuggestionOptions().length > 0) {
                <div class="absolute z-50 mt-1 w-full rounded-md border border-border bg-popover shadow-lg max-h-48 overflow-y-auto">
                  @for (suggestion of tagSuggestionOptions(); track suggestion.path; let i = $index) {
                    <button
                      type="button"
                      (click)="addTagFromSuggestion(suggestion)"
                      [class]="
                        'w-full px-3 py-2 text-left text-sm transition-colors cursor-pointer flex items-center gap-2 ' +
                        (i === tagSelectedIndex()
                          ? 'bg-accent text-accent-foreground'
                          : 'text-foreground hover:bg-accent') +
                        (i === 0 ? ' rounded-t-md' : '') +
                        (i === tagSuggestionOptions().length - 1 ? ' rounded-b-md' : '')
                      "
                    >
                      @if (suggestion.effectiveColor) {
                        <span class="h-3 w-3 shrink-0 rounded-full" [style.background-color]="suggestion.effectiveColor"></span>
                      }
                      {{ suggestion.path }}
                    </button>
                  }
                </div>
              }
            </div>
            @if (tagItems().length > 0) {
              <div class="flex flex-col gap-1">
                @for (item of tagItems(); track item.path) {
                  <label class="flex items-center gap-2 py-0.5 cursor-pointer">
                    <hlm-checkbox
                      [checked]="item.checked"
                      [indeterminate]="item.indeterminate"
                      (checkedChange)="onTagCheckboxChange(item.path, $event)"
                    />
                    <app-tag-badge [path]="item.path" [color]="item.color" />
                  </label>
                }
              </div>
            }
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Parties</label>
            <div class="relative" #partyContainer>
              <input
                hlmInput
                #partySearchEl
                [value]="partySearchText()"
                (input)="onPartySearchInput($event)"
                (focus)="onPartySearchFocus()"
                (keydown)="onPartyKeyDown($event)"
                placeholder="Search parties to add..."
                class="w-full"
              />
              @if (showPartySuggestions() && partySuggestionOptions().length > 0) {
                <div class="absolute z-50 mt-1 w-full rounded-md border border-border bg-popover shadow-lg max-h-48 overflow-y-auto">
                  @for (option of partySuggestionOptions(); track option.id; let i = $index) {
                    <button
                      type="button"
                      (click)="selectPartyOption(option)"
                      [class]="
                        'w-full px-3 py-2 text-left text-sm transition-colors cursor-pointer ' +
                        (i === partySelectedIndex()
                          ? 'bg-accent text-accent-foreground'
                          : 'text-foreground hover:bg-accent') +
                        (i === 0 ? ' rounded-t-md' : '') +
                        (i === partySuggestionOptions().length - 1 ? ' rounded-b-md' : '')
                      "
                    >
                      @if (option.id === createPartyId) {
                        <span class="italic">Create "{{ option.name }}"</span>
                      } @else {
                        {{ option.name }}
                      }
                    </button>
                  }
                </div>
              }
            </div>
            @if (partyItems().length > 0) {
              <div class="flex flex-col gap-1">
                @for (item of partyItems(); track item.id) {
                  <label class="flex items-center gap-2 py-0.5 cursor-pointer">
                    <hlm-checkbox
                      [checked]="item.checked"
                      [indeterminate]="item.indeterminate"
                      (checkedChange)="onPartyCheckboxChange(item.id, $event)"
                    />
                    <app-party-badge [name]="item.name" />
                  </label>
                }
              </div>
            }
          </div>

          @if (applyError()) {
            <p class="text-sm text-destructive">Failed to update documents. Please try again.</p>
          }
          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!hasChanges() || applying()">
              {{ applying() ? 'Applying...' : 'Apply' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class BulkUpdateDialogComponent {
  protected readonly createPartyId = CREATE_PARTY_ID;

  selectedIds = input.required<Set<string>>();
  documents = input.required<GetDocumentsResponse[]>();
  applied = output();

  tagItems = signal<TagCheckboxItem[]>([]);
  partyItems = signal<PartyCheckboxItem[]>([]);
  applying = signal(false);
  applyError = signal(false);

  // Tag search
  tagSearchText = signal('');
  showTagSuggestions = signal(false);
  tagSelectedIndex = signal(0);

  // Party search
  partySearchText = signal('');
  showPartySuggestions = signal(false);
  partySelectedIndex = signal(0);
  creatingParty = signal(false);

  private dialog = viewChild(BrnDialog);
  private tagContainer = viewChild<ElementRef<HTMLDivElement>>('tagContainer');
  private partyContainer = viewChild<ElementRef<HTMLDivElement>>('partyContainer');
  private tagSearchEl = viewChild<ElementRef<HTMLInputElement>>('tagSearchEl');
  private partySearchEl = viewChild<ElementRef<HTMLInputElement>>('partySearchEl');
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  // Tag autocomplete
  private tagSuggestions = toSignal(
    toObservable(this.tagSearchText).pipe(
      debounceTime(200),
      switchMap((text) => {
        if (!text) return of([]);
        return getTags(this.http, this.apiConfig.rootUrl, {
          Search: text,
          IncludeChildren: false,
        }).pipe(map((r) => r.body as GetTagsResponse[]));
      }),
    ),
    { initialValue: [] as GetTagsResponse[] },
  );

  tagSuggestionOptions = computed(() => {
    const existing = new Set(this.tagItems().map((t) => t.path.toLowerCase()));
    return (this.tagSuggestions() ?? []).filter((s) => !existing.has(s.path.toLowerCase()));
  });

  // Party autocomplete
  private partySuggestions = toSignal(
    toObservable(this.partySearchText).pipe(
      debounceTime(200),
      switchMap((text) => {
        if (!text) return of([]);
        return getParties(this.http, this.apiConfig.rootUrl, {
          Search: text,
          Page: 1,
          PageSize: 10,
        }).pipe(map((r) => r.body.items as GetPartiesResponse[]));
      }),
    ),
    { initialValue: [] as GetPartiesResponse[] },
  );

  partySuggestionOptions = computed(() => {
    const existingIds = new Set(this.partyItems().map((p) => p.id));
    const filtered = (this.partySuggestions() ?? []).filter((s) => !existingIds.has(s.id));
    const options: { id: string; name: string }[] = filtered.map((s) => ({ id: s.id, name: s.name }));
    const text = this.partySearchText().trim();
    if (text && !filtered.some((s) => s.name.toLowerCase() === text.toLowerCase())) {
      options.push({ id: CREATE_PARTY_ID, name: text });
    }
    return options;
  });

  hasChanges = computed(() => {
    return this.tagItems().some((t) => this.isChanged(t)) ||
      this.partyItems().some((p) => this.isChanged(p));
  });

  constructor() {
    effect(() => {
      this.tagSearchText();
      if (this.tagSelectedIndex() !== 0) this.tagSelectedIndex.set(0);
    });
    effect(() => {
      this.partySearchText();
      if (this.partySelectedIndex() !== 0) this.partySelectedIndex.set(0);
    });
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.initializeState();
    }
  }

  initializeState(): void {
    const selected = this.selectedIds();
    const docs = this.documents().filter((d) => selected.has(d.id));
    const selectedCount = docs.length;

    // Compute tag states
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

    const tagItems: TagCheckboxItem[] = [];
    for (const [, entry] of tagCounts) {
      if (entry.count === selectedCount) {
        tagItems.push({ path: entry.path, color: entry.color, initial: 'all', checked: true, indeterminate: false });
      } else {
        tagItems.push({ path: entry.path, color: entry.color, initial: 'some', checked: false, indeterminate: true });
      }
    }
    this.tagItems.set(tagItems);

    // Compute party states
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

    const partyItems: PartyCheckboxItem[] = [];
    for (const [id, entry] of partyCounts) {
      if (entry.count === selectedCount) {
        partyItems.push({ id, name: entry.name, initial: 'all', checked: true, indeterminate: false });
      } else {
        partyItems.push({ id, name: entry.name, initial: 'some', checked: false, indeterminate: true });
      }
    }
    this.partyItems.set(partyItems);

    this.tagSearchText.set('');
    this.partySearchText.set('');
    this.applying.set(false);
    this.applyError.set(false);
    this.showTagSuggestions.set(false);
    this.showPartySuggestions.set(false);
  }

  // --- Tag checkbox ---
  onTagCheckboxChange(path: string, checked: boolean): void {
    this.tagItems.update((items) =>
      items.map((item) =>
        item.path === path ? { ...item, checked, indeterminate: false } : item,
      ),
    );
  }

  // --- Party checkbox ---
  onPartyCheckboxChange(id: string, checked: boolean): void {
    this.partyItems.update((items) =>
      items.map((item) =>
        item.id === id ? { ...item, checked, indeterminate: false } : item,
      ),
    );
  }

  // --- Tag search ---
  onTagSearchInput(event: Event): void {
    this.tagSearchText.set((event.target as HTMLInputElement).value);
    this.showTagSuggestions.set(true);
  }

  onTagSearchFocus(): void {
    if (this.tagSearchText()) {
      this.showTagSuggestions.set(true);
    }
  }

  onTagKeyDown(event: KeyboardEvent): void {
    const options = this.tagSuggestionOptions();
    if (event.key === 'Enter') {
      event.preventDefault();
      if (this.showTagSuggestions() && options.length > 0 && options[this.tagSelectedIndex()]) {
        this.addTagFromSuggestion(options[this.tagSelectedIndex()]);
      }
    } else if (event.key === 'ArrowDown' && this.showTagSuggestions()) {
      event.preventDefault();
      this.tagSelectedIndex.update((i) => (i < options.length - 1 ? i + 1 : i));
    } else if (event.key === 'ArrowUp' && this.showTagSuggestions()) {
      event.preventDefault();
      this.tagSelectedIndex.update((i) => (i > 0 ? i - 1 : 0));
    } else if (event.key === 'Escape') {
      this.showTagSuggestions.set(false);
    }
  }

  addTagFromSuggestion(suggestion: GetTagsResponse): void {
    const existingIndex = this.tagItems().findIndex(
      (t) => t.path.toLowerCase() === suggestion.path.toLowerCase(),
    );
    if (existingIndex >= 0) {
      // Already in list — set to checked
      this.tagItems.update((items) =>
        items.map((item, i) =>
          i === existingIndex ? { ...item, checked: true, indeterminate: false } : item,
        ),
      );
    } else {
      // New tag — add as checked
      this.tagItems.update((items) => [
        ...items,
        {
          path: suggestion.path,
          color: suggestion.effectiveColor ?? null,
          initial: 'new' as const,
          checked: true,
          indeterminate: false,
        },
      ]);
    }
    this.tagSearchText.set('');
    this.showTagSuggestions.set(false);
    this.tagSearchEl()?.nativeElement.focus();
  }

  // --- Party search ---
  onPartySearchInput(event: Event): void {
    this.partySearchText.set((event.target as HTMLInputElement).value);
    this.showPartySuggestions.set(true);
  }

  onPartySearchFocus(): void {
    if (this.partySearchText()) {
      this.showPartySuggestions.set(true);
    }
  }

  onPartyKeyDown(event: KeyboardEvent): void {
    const options = this.partySuggestionOptions();
    if (event.key === 'Enter') {
      event.preventDefault();
      if (this.showPartySuggestions() && options.length > 0 && options[this.partySelectedIndex()]) {
        this.selectPartyOption(options[this.partySelectedIndex()]);
      }
    } else if (event.key === 'ArrowDown' && this.showPartySuggestions()) {
      event.preventDefault();
      this.partySelectedIndex.update((i) => (i < options.length - 1 ? i + 1 : i));
    } else if (event.key === 'ArrowUp' && this.showPartySuggestions()) {
      event.preventDefault();
      this.partySelectedIndex.update((i) => (i > 0 ? i - 1 : 0));
    } else if (event.key === 'Escape') {
      this.showPartySuggestions.set(false);
    }
  }

  selectPartyOption(option: { id: string; name: string }): void {
    if (option.id === CREATE_PARTY_ID) {
      this.createAndAddParty(option.name);
    } else {
      this.addPartyFromSuggestion(option);
    }
  }

  addPartyFromSuggestion(party: { id: string; name: string }): void {
    const existingIndex = this.partyItems().findIndex((p) => p.id === party.id);
    if (existingIndex >= 0) {
      this.partyItems.update((items) =>
        items.map((item, i) =>
          i === existingIndex ? { ...item, checked: true, indeterminate: false } : item,
        ),
      );
    } else {
      this.partyItems.update((items) => [
        ...items,
        {
          id: party.id,
          name: party.name,
          initial: 'new' as const,
          checked: true,
          indeterminate: false,
        },
      ]);
    }
    this.partySearchText.set('');
    this.showPartySuggestions.set(false);
    this.partySearchEl()?.nativeElement.focus();
  }

  private createAndAddParty(name: string): void {
    if (this.creatingParty()) return;
    this.creatingParty.set(true);
    createParty(this.http, this.apiConfig.rootUrl, {
      body: { name },
    }).subscribe({
      next: (r) => {
        this.creatingParty.set(false);
        this.addPartyFromSuggestion({ id: r.body.id, name: r.body.name });
      },
      error: () => {
        this.creatingParty.set(false);
      },
    });
  }

  // --- Click outside ---
  onClickOutside(event: MouseEvent): void {
    if (!this.showTagSuggestions() && !this.showPartySuggestions()) return;

    const target = event.target as Node;
    if (this.showTagSuggestions()) {
      const tagEl = this.tagContainer()?.nativeElement;
      if (tagEl && !tagEl.contains(target)) {
        this.showTagSuggestions.set(false);
      }
    }
    if (this.showPartySuggestions()) {
      const partyEl = this.partyContainer()?.nativeElement;
      if (partyEl && !partyEl.contains(target)) {
        this.showPartySuggestions.set(false);
      }
    }
  }

  // --- Submit ---
  handleSubmit(event: Event): void {
    event.preventDefault();
    if (!this.hasChanges()) return;

    const tagPathsToAdd = this.tagItems()
      .filter((t) => t.checked && !t.indeterminate && t.initial !== 'all')
      .map((t) => t.path);
    const tagPathsToRemove = this.tagItems()
      .filter((t) => !t.checked && !t.indeterminate && t.initial !== 'new')
      .map((t) => t.path);

    const partyIdsToAdd = this.partyItems()
      .filter((p) => p.checked && !p.indeterminate && p.initial !== 'all')
      .map((p) => p.id);
    const partyIdsToRemove = this.partyItems()
      .filter((p) => !p.checked && !p.indeterminate && p.initial !== 'new')
      .map((p) => p.id);

    const ids = Array.from(this.selectedIds());
    this.applying.set(true);
    this.applyError.set(false);

    bulkUpdateDocuments(this.http, this.apiConfig.rootUrl, {
      body: {
        ids,
        tagPaths: tagPathsToAdd.length > 0 ? tagPathsToAdd : null,
        partyIds: partyIdsToAdd.length > 0 ? partyIdsToAdd : null,
        removeTagPaths: tagPathsToRemove.length > 0 ? tagPathsToRemove : null,
        removePartyIds: partyIdsToRemove.length > 0 ? partyIdsToRemove : null,
      },
    }).subscribe({
      next: (response) => {
        this.applying.set(false);
        const count = +response.body.updatedCount;
        toast.success(`Updated ${count} document${count > 1 ? 's' : ''}.`);
        this.dialog()?.close();
        this.applied.emit();
      },
      error: () => {
        this.applying.set(false);
        this.applyError.set(true);
        toast.error('Failed to update documents. Please try again.');
      },
    });
  }

  private isChanged(item: TagCheckboxItem | PartyCheckboxItem): boolean {
    if (item.initial === 'new') return true;
    if (item.initial === 'all' && !item.checked) return true;
    if (item.initial === 'some' && !item.indeterminate) return true;
    return false;
  }
}
