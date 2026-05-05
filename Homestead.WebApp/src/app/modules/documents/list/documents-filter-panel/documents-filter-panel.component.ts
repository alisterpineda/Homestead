import { Component, ChangeDetectionStrategy, inject, input, output, signal, computed, effect, WritableSignal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideListFilter } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { HlmSidebarService } from '@/shared/components/spartan/sidebar';

export interface FilterOption {
  id: string;
  label: string;
}

@Component({
  selector: 'app-documents-filter-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    NgIcon,
    HlmIconImports,
    HlmPopoverImports,
    HlmSheetImports,
    HlmCheckboxImports,
    HlmSeparatorImports,
    HlmInput,
    HlmButton,
    HlmBadge,
  ],
  providers: [provideIcons({ lucideListFilter })],
  template: `
    @if (!isMobile()) {
      <div hlmPopover>
        <button hlmPopoverTrigger hlmBtn variant="outline" size="sm" aria-label="Filter documents">
          <ng-icon hlm name="lucideListFilter" size="sm" class="text-muted-foreground" />
          <span>{{ triggerLabel() }}</span>
        </button>
        <div hlmPopoverContent *hlmPopoverPortal class="w-80 p-0" align="start">
          <ng-container *ngTemplateOutlet="panelBody" />
        </div>
      </div>
    } @else {
      <hlm-sheet side="bottom">
        <button hlmSheetTrigger hlmBtn variant="outline" size="sm" aria-label="Filter documents">
          <ng-icon hlm name="lucideListFilter" size="sm" class="text-muted-foreground" />
          @if (activeCount() > 0) {
            <span hlmBadge variant="secondary" class="font-normal">{{ activeCount() }}</span>
          }
        </button>
        <hlm-sheet-content *hlmSheetPortal class="p-0" [showCloseButton]="false">
          <ng-container *ngTemplateOutlet="panelBody" />
        </hlm-sheet-content>
      </hlm-sheet>
    }

    <ng-template #panelBody>
      <div class="flex items-center justify-between px-3 py-2 border-b border-border">
        <span class="text-sm font-medium">Filters</span>
        <button
          hlmBtn
          variant="ghost"
          size="sm"
          class="h-7 px-2"
          [disabled]="!hasActiveFilters()"
          (click)="onReset()"
        >
          Reset
        </button>
      </div>

      <div class="p-3 border-b border-border">
        <div class="mb-2 text-xs font-medium text-muted-foreground">Tags</div>
        <input
          hlmInput
          class="h-8 w-full mb-2"
          placeholder="Search tags..."
          [value]="tagSearch()"
          (input)="onTagSearchInput($event)"
        />
        <div class="max-h-[180px] overflow-y-auto -mx-1">
          @let tagDisplay = displayTagOptions();
          @for (option of tagDisplay.selected; track option.id) {
            <button
              type="button"
              class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
              (click)="onToggleTag(option.id)"
            >
              <hlm-checkbox class="pointer-events-none" [checked]="true" aria-hidden="true" />
              <span class="truncate">{{ option.label }}</span>
            </button>
          }
          @if (tagDisplay.selected.length > 0 && tagDisplay.unselected.length > 0) {
            <div class="my-1 border-t border-border" aria-hidden="true"></div>
          }
          @for (option of tagDisplay.unselected; track option.id) {
            <button
              type="button"
              class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
              (click)="onToggleTag(option.id)"
            >
              <hlm-checkbox class="pointer-events-none" [checked]="false" aria-hidden="true" />
              <span class="truncate">{{ option.label }}</span>
            </button>
          }
          @if (tagDisplay.selected.length === 0 && tagDisplay.unselected.length === 0) {
            @if (tagOptionsLoading()) {
              <p class="py-3 text-center text-sm text-muted-foreground">Loading...</p>
            } @else {
              <p class="py-3 text-center text-sm text-muted-foreground">No tags found.</p>
            }
          }
        </div>
        <label class="mt-2 flex w-full items-center gap-2 rounded-sm px-1 py-1.5 text-sm cursor-pointer">
          <hlm-checkbox
            [checked]="includeChildTags()"
            (checkedChange)="includeChildTagsChange.emit($event)"
          />
          <span>Include sub-tags</span>
        </label>
      </div>

      <div class="p-3">
        <div class="mb-2 text-xs font-medium text-muted-foreground">Parties</div>
        <input
          hlmInput
          class="h-8 w-full mb-2"
          placeholder="Search parties..."
          [value]="partySearch()"
          (input)="onPartySearchInput($event)"
        />
        <div class="max-h-[180px] overflow-y-auto -mx-1">
          @let partyDisplay = displayPartyOptions();
          @for (option of partyDisplay.selected; track option.id) {
            <button
              type="button"
              class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
              (click)="onToggleParty(option.id)"
            >
              <hlm-checkbox class="pointer-events-none" [checked]="true" aria-hidden="true" />
              <span class="truncate">{{ option.label }}</span>
            </button>
          }
          @if (partyDisplay.selected.length > 0 && partyDisplay.unselected.length > 0) {
            <div class="my-1 border-t border-border" aria-hidden="true"></div>
          }
          @for (option of partyDisplay.unselected; track option.id) {
            <button
              type="button"
              class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
              (click)="onToggleParty(option.id)"
            >
              <hlm-checkbox class="pointer-events-none" [checked]="false" aria-hidden="true" />
              <span class="truncate">{{ option.label }}</span>
            </button>
          }
          @if (partyDisplay.selected.length === 0 && partyDisplay.unselected.length === 0) {
            @if (partyOptionsLoading()) {
              <p class="py-3 text-center text-sm text-muted-foreground">Loading...</p>
            } @else {
              <p class="py-3 text-center text-sm text-muted-foreground">No parties found.</p>
            }
          }
        </div>
      </div>
    </ng-template>
  `,
})
export class DocumentsFilterPanelComponent {
  tagOptions = input<FilterOption[]>([]);
  partyOptions = input<FilterOption[]>([]);
  selectedTagIds = input<string[]>([]);
  selectedPartyIds = input<string[]>([]);
  includeChildTags = input(false);
  tagOptionsLoading = input(false);
  partyOptionsLoading = input(false);

  tagSelectionChange = output<string[]>();
  partySelectionChange = output<string[]>();
  includeChildTagsChange = output<boolean>();
  tagSearchChange = output<string>();
  partySearchChange = output<string>();
  reset = output<void>();

  private sidebarService = inject(HlmSidebarService);
  isMobile = this.sidebarService.isMobile;

  tagSearch = signal('');
  partySearch = signal('');

  // Cache labels for selected items so they remain visible (pinned) even when
  // a search filter excludes them from the current `tagOptions`/`partyOptions`.
  private tagLabelCache = signal<Map<string, string>>(new Map());
  private partyLabelCache = signal<Map<string, string>>(new Map());

  constructor() {
    effect(() => this.mergeLabelCache(this.tagLabelCache, this.tagOptions()));
    effect(() => this.mergeLabelCache(this.partyLabelCache, this.partyOptions()));
  }

  private mergeLabelCache(cache: WritableSignal<Map<string, string>>, opts: FilterOption[]): void {
    if (opts.length === 0) return;
    cache.update(prev => {
      let changed = false;
      const next = new Map(prev);
      for (const o of opts) {
        if (next.get(o.id) !== o.label) {
          next.set(o.id, o.label);
          changed = true;
        }
      }
      return changed ? next : prev;
    });
  }

  displayTagOptions = computed(() => this.partition(this.selectedTagIds(), this.tagOptions(), this.tagLabelCache()));
  displayPartyOptions = computed(() => this.partition(this.selectedPartyIds(), this.partyOptions(), this.partyLabelCache()));

  private partition(selectedIds: string[], opts: FilterOption[], cache: Map<string, string>): { selected: FilterOption[]; unselected: FilterOption[] } {
    const optMap = new Map(opts.map(o => [o.id, o]));
    const selected: FilterOption[] = [];
    for (const id of selectedIds) {
      const fromOpts = optMap.get(id);
      if (fromOpts) {
        selected.push(fromOpts);
      } else {
        const cachedLabel = cache.get(id);
        if (cachedLabel !== undefined) selected.push({ id, label: cachedLabel });
      }
    }
    const selectedSet = new Set(selectedIds);
    const unselected = opts.filter(o => !selectedSet.has(o.id));
    return { selected, unselected };
  }

  hasTagFilter = computed(() => this.selectedTagIds().length > 0 || this.includeChildTags());
  hasPartyFilter = computed(() => this.selectedPartyIds().length > 0);
  hasActiveFilters = computed(() => this.hasTagFilter() || this.hasPartyFilter());

  activeCount = computed(() => {
    let count = 0;
    if (this.hasTagFilter()) count++;
    if (this.hasPartyFilter()) count++;
    return count;
  });

  triggerLabel = computed(() => {
    const dims: string[] = [];
    if (this.hasTagFilter()) dims.push('Tags');
    if (this.hasPartyFilter()) dims.push('Parties');
    if (dims.length === 0) return 'Filter';
    return `Filter: ${dims.join(', ')}`;
  });

  onToggleTag(id: string): void {
    const current = this.selectedTagIds();
    const next = current.includes(id) ? current.filter(i => i !== id) : [...current, id];
    this.tagSelectionChange.emit(next);
  }

  onToggleParty(id: string): void {
    const current = this.selectedPartyIds();
    const next = current.includes(id) ? current.filter(i => i !== id) : [...current, id];
    this.partySelectionChange.emit(next);
  }

  onTagSearchInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.tagSearch.set(value);
    this.tagSearchChange.emit(value);
  }

  onPartySearchInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.partySearch.set(value);
    this.partySearchChange.emit(value);
  }

  onReset(): void {
    this.tagSearch.set('');
    this.partySearch.set('');
    this.reset.emit();
  }
}
