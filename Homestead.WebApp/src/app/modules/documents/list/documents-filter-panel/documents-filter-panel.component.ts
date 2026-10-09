import { Component, ChangeDetectionStrategy, input, output, signal, computed, effect, WritableSignal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideListFilter } from '@ng-icons/lucide';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmDrawerImports } from '@/shared/components/spartan/drawer';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { HlmComboboxImports } from '@/shared/components/spartan/combobox';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { injectIsNarrowViewport } from '@/shared/utils/viewport';

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
    HlmPopoverImports,
    HlmDrawerImports,
    HlmCheckboxImports,
    HlmSeparatorImports,
    HlmComboboxImports,
    HlmButton,
    HlmBadge,
  ],
  providers: [provideIcons({ lucideListFilter })],
  template: `
    @if (!narrow()) {
      <div hlmPopover>
        <button hlmPopoverTrigger hlmBtn variant="outline" aria-label="Filter documents">
          <ng-icon name="lucideListFilter" class="text-[length:--spacing(4)] text-muted-foreground" />
          <span>{{ triggerLabel() }}</span>
        </button>
        <div hlmPopoverContent *hlmPopoverPortal class="w-80 p-0" align="start">
          <ng-container *ngTemplateOutlet="panelBody" />
        </div>
      </div>
    } @else {
      <hlm-drawer>
        <button hlmDrawerTrigger hlmBtn variant="outline" aria-label="Filter documents">
          <ng-icon name="lucideListFilter" class="text-[length:--spacing(4)] text-muted-foreground" />
          @if (activeCount() > 0) {
            <span hlmBadge variant="secondary" class="font-normal">{{ activeCount() }}</span>
          }
        </button>
        <hlm-drawer-content *hlmDrawerPortal>
          <h3 hlmDrawerTitle class="sr-only">Filter documents</h3>
          <ng-container *ngTemplateOutlet="panelBody" />
        </hlm-drawer-content>
      </hlm-drawer>
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
        <hlm-combobox-multiple
          [value]="selectedTagIds()"
          (valueChange)="tagSelectionChange.emit($event ?? [])"
          [search]="tagSearch()"
          (searchChange)="onTagSearchChange($event)"
          [filter]="passThroughFilter"
        >
          <hlm-combobox-chips>
            <ng-template hlmComboboxValues let-values>
              @for (id of values; track id) {
                <hlm-combobox-chip [value]="id">{{ getTagLabel(id) }}</hlm-combobox-chip>
              }
            </ng-template>
            <input hlmComboboxChipInput placeholder="Search tags..." />
          </hlm-combobox-chips>
          <hlm-combobox-content *hlmComboboxPortal>
            @if (tagOptionsLoading()) {
              <p class="py-3 text-center text-sm text-muted-foreground">Loading...</p>
            } @else {
              <hlm-combobox-empty>No tags found.</hlm-combobox-empty>
              <div hlmComboboxList>
                @for (option of unselectedTagOptions(); track option.id) {
                  <hlm-combobox-item [value]="option.id">{{ option.label }}</hlm-combobox-item>
                }
              </div>
            }
          </hlm-combobox-content>
        </hlm-combobox-multiple>

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
        <hlm-combobox-multiple
          [value]="selectedPartyIds()"
          (valueChange)="partySelectionChange.emit($event ?? [])"
          [search]="partySearch()"
          (searchChange)="onPartySearchChange($event)"
          [filter]="passThroughFilter"
        >
          <hlm-combobox-chips>
            <ng-template hlmComboboxValues let-values>
              @for (id of values; track id) {
                <hlm-combobox-chip [value]="id">{{ getPartyLabel(id) }}</hlm-combobox-chip>
              }
            </ng-template>
            <input hlmComboboxChipInput placeholder="Search parties..." />
          </hlm-combobox-chips>
          <hlm-combobox-content *hlmComboboxPortal>
            @if (partyOptionsLoading()) {
              <p class="py-3 text-center text-sm text-muted-foreground">Loading...</p>
            } @else {
              <hlm-combobox-empty>No parties found.</hlm-combobox-empty>
              <div hlmComboboxList>
                @for (option of unselectedPartyOptions(); track option.id) {
                  <hlm-combobox-item [value]="option.id">{{ option.label }}</hlm-combobox-item>
                }
              </div>
            }
          </hlm-combobox-content>
        </hlm-combobox-multiple>
      </div>
    </ng-template>
  `,
})
export class DocumentsFilterPanelComponent {
  tagOptions = input<FilterOption[]>([]);
  partyOptions = input<FilterOption[]>([]);
  resolvedTagOptions = input<FilterOption[]>([]);
  resolvedPartyOptions = input<FilterOption[]>([]);
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

  protected readonly narrow = injectIsNarrowViewport();

  tagSearch = signal('');
  partySearch = signal('');

  // Server-side filtering is authoritative; skip the combobox's client-side filter.
  passThroughFilter = (): boolean => true;

  // Cache labels for selected items so chips keep their text even when a search
  // filter excludes them from the current `tagOptions`/`partyOptions`.
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

  unselectedTagOptions = computed(() => this.excludeSelected(this.tagOptions(), this.selectedTagIds()));
  unselectedPartyOptions = computed(() => this.excludeSelected(this.partyOptions(), this.selectedPartyIds()));

  private excludeSelected(opts: FilterOption[], selectedIds: string[]): FilterOption[] {
    const selectedSet = new Set(selectedIds);
    return opts.filter(o => !selectedSet.has(o.id));
  }

  getTagLabel(id: string): string {
    return this.lookupLabel(id, this.tagOptions(), this.resolvedTagOptions(), this.tagLabelCache());
  }

  getPartyLabel(id: string): string {
    return this.lookupLabel(id, this.partyOptions(), this.resolvedPartyOptions(), this.partyLabelCache());
  }

  private lookupLabel(id: string, opts: FilterOption[], resolved: FilterOption[], cache: Map<string, string>): string {
    return opts.find(o => o.id === id)?.label
      ?? resolved.find(o => o.id === id)?.label
      ?? cache.get(id)
      ?? id;
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

  onTagSearchChange(value: string): void {
    this.tagSearch.set(value);
    this.tagSearchChange.emit(value);
  }

  onPartySearchChange(value: string): void {
    this.partySearch.set(value);
    this.partySearchChange.emit(value);
  }

  onReset(): void {
    this.tagSearch.set('');
    this.partySearch.set('');
    this.reset.emit();
  }
}
