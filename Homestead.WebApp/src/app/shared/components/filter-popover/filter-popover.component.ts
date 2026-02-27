import { Component, ChangeDetectionStrategy, input, output, signal, computed } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCirclePlus } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmButton } from '@/shared/components/spartan/button';

export interface FilterOption {
  id: string;
  label: string;
}

@Component({
  selector: 'app-filter-popover',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmPopoverImports,
    HlmCheckboxImports,
    HlmBadge,
    HlmSeparatorImports,
    HlmInput,
    HlmButton,
  ],
  providers: [provideIcons({ lucideCirclePlus })],
  template: `
    <div hlmPopover>
      <button hlmPopoverTrigger hlmBtn variant="outline" size="sm" class="border-dashed">
        <ng-icon hlm name="lucideCirclePlus" size="sm" class="text-muted-foreground" />
        {{ label() }}
        @if (visibleSelections().length > 0) {
          <span hlmSeparator orientation="vertical" class="mx-0.5 h-4"></span>
          @for (item of visibleSelections().slice(0, 2); track item.id) {
            <span hlmBadge variant="secondary" class="font-normal">{{ item.label }}</span>
          }
          @if (visibleSelections().length > 2) {
            <span hlmBadge variant="secondary" class="font-normal">+{{ visibleSelections().length - 2 }}</span>
          }
        }
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-[200px] p-0" align="start">
        <div class="p-2">
          <input
            hlmInput
            class="h-8 w-full"
            [placeholder]="searchPlaceholder()"
            [value]="searchText()"
            (input)="onSearchInput($event)"
          />
        </div>
        <span hlmSeparator></span>
        <div class="max-h-[200px] overflow-y-auto p-1">
          @if (loading()) {
            <p class="py-4 text-center text-sm text-muted-foreground">Loading...</p>
          } @else if (filteredOptions().length === 0) {
            <p class="py-4 text-center text-sm text-muted-foreground">No results found.</p>
          } @else {
            @for (option of filteredOptions(); track option.id) {
              <button
                class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer"
                (click)="toggleOption(option.id)"
              >
                <hlm-checkbox
                  class="pointer-events-none"
                  [checked]="isSelected(option.id)"
                  aria-hidden="true"
                />
                <span>{{ option.label }}</span>
              </button>
            }
          }
        </div>
        @if (selectedIds().length > 0) {
          <span hlmSeparator></span>
          <div class="p-1">
            <button
              class="flex w-full justify-center rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer"
              (click)="clearAll()"
            >
              Clear filters
            </button>
          </div>
        }
      </div>
    </div>
  `,
})
export class FilterPopoverComponent {
  label = input.required<string>();
  options = input<FilterOption[]>([]);
  selectedIds = input<string[]>([]);
  searchPlaceholder = input('Search...');
  loading = input(false);

  selectionChange = output<string[]>();
  searchChange = output<string>();

  searchText = signal('');

  visibleSelections = computed(() => {
    const ids = this.selectedIds();
    const opts = this.options();
    return opts.filter(o => ids.includes(o.id));
  });

  filteredOptions = computed(() => {
    const search = this.searchText().toLowerCase();
    if (!search) return this.options();
    return this.options().filter(o => o.label.toLowerCase().includes(search));
  });

  isSelected(id: string): boolean {
    return this.selectedIds().includes(id);
  }

  toggleOption(id: string): void {
    const current = this.selectedIds();
    const next = current.includes(id)
      ? current.filter(i => i !== id)
      : [...current, id];
    this.selectionChange.emit(next);
  }

  clearAll(): void {
    this.searchText.set('');
    this.searchChange.emit('');
    this.selectionChange.emit([]);
  }

  onSearchInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.searchText.set(value);
    this.searchChange.emit(value);
  }
}
