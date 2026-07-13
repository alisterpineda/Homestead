import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  signal,
  computed,
  inject,
  viewChild,
  ElementRef,
  effect,
  untracked,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, debounceTime, of, map, tap, catchError } from 'rxjs';
import { TagBadgeComponent } from '@/shared/components/tag-badge/tag-badge.component';
import { CreateTagDialogComponent } from '../create-tag-dialog/create-tag-dialog.component';
import { InventoryTagApiService } from '@/api/services/inventory-tag-api.service';
import type { GetInventoryTagsResponse } from '@/api/generated/models/get-inventory-tags-response';
import { CREATE_OPTION_ID } from '../tag-input-shared';

@Component({
  selector: 'app-tag-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TagBadgeComponent, CreateTagDialogComponent],
  host: {
    '(document:mousedown)': 'onClickOutside($event)',
  },
  template: `
    <div class="relative" #container>
      <div
        class="flex flex-wrap items-center gap-1.5 rounded-md border border-input bg-background px-2 py-1.5 transition-colors focus-within:border-ring focus-within:ring-2 focus-within:ring-ring/20"
      >
        @for (tag of value(); track tag) {
          <app-tag-badge
            [path]="tag"
            [color]="colorMap().get(tag) ?? null"
            [aiSuggested]="aiPendingTags().has(tag) || aiTransientTags().has(tag)"
            [aiTransient]="aiTransientTags().has(tag)"
            [removable]="!aiPendingTags().has(tag)"
            (removed)="removeTag(tag)"
            (approved)="tagApproved.emit(tag)"
            (declined)="tagDeclined.emit(tag)"
          />
        }
        <input
          #inputEl
          [value]="searchText()"
          (input)="onSearchInput($event)"
          (focus)="onFocus()"
          (keydown)="onKeyDown($event)"
          [placeholder]="value().length === 0 ? 'Add tags...' : ''"
          class="min-w-[80px] flex-1 bg-transparent text-base md:text-sm text-foreground placeholder:text-muted-foreground outline-none"
        />
      </div>

      @if (showSuggestions() && allOptions().length > 0) {
        <div
          class="absolute z-50 mt-1 w-full rounded-md border border-border bg-popover shadow-lg"
        >
          @for (option of allOptions(); track option.id; let i = $index) {
            <button
              type="button"
              (click)="selectOption(option)"
              [class]="
                'w-full px-3 py-2 text-left text-sm transition-colors cursor-pointer flex items-center gap-2 ' +
                (i === selectedIndex()
                  ? 'bg-accent text-accent-foreground'
                  : 'text-foreground hover:bg-accent') +
                (i === 0 ? ' rounded-t-md' : '') +
                (i === allOptions().length - 1 ? ' rounded-b-md' : '')
              "
            >
              @if (option.id === createOptionId) {
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

      <app-create-tag-dialog
        #createTagDialog
        [initialPath]="searchText()"
        [hideTrigger]="true"
        (created)="addTag($event.path, $event.effectiveColor)"
      />
    </div>
  `,
})
export class TagInputComponent {
  protected readonly createOptionId = CREATE_OPTION_ID;

  value = input.required<string[]>();
  initialColorMap = input<Record<string, string>>({});
  aiPendingTags = input<Set<string>>(new Set());
  aiTransientTags = input<Set<string>>(new Set());
  valueChange = output<string[]>();
  tagApproved = output<string>();
  tagDeclined = output<string>();

  searchText = signal('');
  showSuggestions = signal(false);
  selectedIndex = signal(0);
  loading = signal(false);
  private addedColorMap = signal<Map<string, string>>(new Map());

  colorMap = computed(() => {
    const initial = this.initialColorMap();
    const added = this.addedColorMap();
    const merged = new Map<string, string>();
    for (const [k, v] of Object.entries(initial)) {
      if (v) merged.set(k, v);
    }
    for (const [k, v] of added) {
      merged.set(k, v);
    }
    return merged;
  });

  inputEl = viewChild<ElementRef<HTMLInputElement>>('inputEl');
  container = viewChild<ElementRef<HTMLDivElement>>('container');
  private createTagDialog = viewChild<CreateTagDialogComponent>('createTagDialog');

  private tagApi = inject(InventoryTagApiService);

  private suggestions = toSignal(
    toObservable(this.searchText).pipe(
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
          map((r) => r.items as GetInventoryTagsResponse[]),
          tap(() => this.loading.set(false)),
          catchError(() => {
            this.loading.set(false);
            return of([]);
          }),
        );
      }),
    ),
    { initialValue: [] as GetInventoryTagsResponse[] },
  );

  filteredSuggestions = computed(() => {
    const current = new Set(this.value());
    return (this.suggestions() ?? []).filter((s) => !current.has(s.path));
  });

  allOptions = computed(() => {
    const filtered = this.filteredSuggestions();
    const text = this.searchText().trim();

    const lowerText = text.toLowerCase();
    if (
      text &&
      !this.loading() &&
      !filtered.some((s) => s.path.toLowerCase() === lowerText)
    ) {
      return [...filtered, { id: CREATE_OPTION_ID, path: text, effectiveColor: null as string | null }];
    }

    return filtered;
  });

  constructor() {
    effect(() => {
      this.searchText();
      if (untracked(this.selectedIndex) !== 0) this.selectedIndex.set(0);
    });
  }

  selectOption(option: { id: string; path: string; effectiveColor?: string | null }): void {
    if (option.id === CREATE_OPTION_ID) {
      this.showSuggestions.set(false);
      this.createTagDialog()?.open();
    } else {
      this.addTag(option.path, option.effectiveColor);
    }
  }

  addTag(path: string, color?: string | null): void {
    const normalized = path.trim();
    if (normalized && !this.value().includes(normalized)) {
      this.valueChange.emit([...this.value(), normalized]);
      if (color) {
        this.addedColorMap.update((m) => {
          const next = new Map(m);
          next.set(normalized, color);
          return next;
        });
      }
    }
    this.searchText.set('');
    this.showSuggestions.set(false);
    this.inputEl()?.nativeElement.focus();
  }

  removeTag(path: string): void {
    this.valueChange.emit(this.value().filter((t) => t !== path));
    this.addedColorMap.update((m) => {
      const next = new Map(m);
      next.delete(path);
      return next;
    });
  }

  onSearchInput(event: Event): void {
    const val = (event.target as HTMLInputElement).value;
    this.searchText.set(val);
    this.loading.set(!!val.trim());
    this.showSuggestions.set(true);
  }

  onFocus(): void {
    if (this.searchText()) {
      this.showSuggestions.set(true);
    }
  }

  onKeyDown(event: KeyboardEvent): void {
    const options = this.allOptions();
    if (event.key === 'Enter') {
      event.preventDefault();
      if (this.showSuggestions() && options.length > 0 && options[this.selectedIndex()]) {
        this.selectOption(options[this.selectedIndex()]);
      }
    } else if (event.key === 'Backspace' && this.searchText() === '' && this.value().length > 0) {
      const lastTag = this.value()[this.value().length - 1];
      if (!this.aiPendingTags().has(lastTag)) {
        this.removeTag(lastTag);
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

  onClickOutside(event: MouseEvent): void {
    if (this.container() && !this.container()!.nativeElement.contains(event.target as Node)) {
      this.showSuggestions.set(false);
    }
  }
}
