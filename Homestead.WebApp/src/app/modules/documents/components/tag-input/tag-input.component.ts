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
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, debounceTime, of, map } from 'rxjs';
import { TagBadgeComponent } from '../tag-badge/tag-badge.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getTags } from '@/api/generated/fn/tags/get-tags';
import type { GetTagsResponse } from '@/api/generated/models/get-tags-response';

@Component({
  selector: 'app-tag-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TagBadgeComponent],
  host: {
    '(document:mousedown)': 'onClickOutside($event)',
  },
  template: `
    <div class="relative" #container>
      <div
        class="flex flex-wrap items-center gap-1.5 rounded-md border border-input bg-background px-2 py-1.5 transition-colors focus-within:border-ring focus-within:ring-2 focus-within:ring-ring/20"
      >
        @for (tag of value(); track tag) {
          <app-tag-badge [path]="tag" [removable]="true" (removed)="removeTag(tag)" />
        }
        <input
          #inputEl
          [value]="searchText()"
          (input)="onSearchInput($event)"
          (focus)="onFocus()"
          (keydown)="onKeyDown($event)"
          [placeholder]="value().length === 0 ? 'Add tags...' : ''"
          class="min-w-[80px] flex-1 bg-transparent text-sm text-foreground placeholder:text-muted-foreground outline-none"
        />
      </div>

      @if (showSuggestions() && filteredSuggestions().length > 0) {
        <div
          class="absolute z-50 mt-1 w-full rounded-md border border-border bg-popover shadow-lg"
        >
          @for (suggestion of filteredSuggestions(); track suggestion.id; let i = $index) {
            <button
              type="button"
              (click)="addTag(suggestion.path)"
              [class]="
                'w-full px-3 py-2 text-left text-sm transition-colors cursor-pointer ' +
                (i === selectedIndex()
                  ? 'bg-accent text-accent-foreground'
                  : 'text-foreground hover:bg-accent') +
                (i === 0 ? ' rounded-t-md' : '') +
                (i === filteredSuggestions().length - 1 ? ' rounded-b-md' : '')
              "
            >
              {{ suggestion.path }}
            </button>
          }
        </div>
      }
    </div>
  `,
})
export class TagInputComponent {
  value = input.required<string[]>();
  valueChange = output<string[]>();

  searchText = signal('');
  showSuggestions = signal(false);
  selectedIndex = signal(0);

  inputEl = viewChild<ElementRef<HTMLInputElement>>('inputEl');
  container = viewChild<ElementRef<HTMLDivElement>>('container');

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  private suggestions = toSignal(
    toObservable(this.searchText).pipe(
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

  filteredSuggestions = computed(() => {
    const current = this.value();
    return (this.suggestions() ?? []).filter((s) => !current.includes(s.path));
  });

  constructor() {
    effect(() => {
      this.searchText();
      this.selectedIndex.set(0);
    });
  }

  addTag(path: string): void {
    const normalized = path.trim().toLowerCase();
    if (normalized && !this.value().includes(normalized)) {
      this.valueChange.emit([...this.value(), normalized]);
    }
    this.searchText.set('');
    this.showSuggestions.set(false);
    this.inputEl()?.nativeElement.focus();
  }

  removeTag(path: string): void {
    this.valueChange.emit(this.value().filter((t) => t !== path));
  }

  onSearchInput(event: Event): void {
    const val = (event.target as HTMLInputElement).value;
    this.searchText.set(val);
    this.showSuggestions.set(true);
  }

  onFocus(): void {
    if (this.searchText()) {
      this.showSuggestions.set(true);
    }
  }

  onKeyDown(event: KeyboardEvent): void {
    const filtered = this.filteredSuggestions();
    if (event.key === 'Enter') {
      event.preventDefault();
      if (this.showSuggestions() && filtered.length > 0 && filtered[this.selectedIndex()]) {
        this.addTag(filtered[this.selectedIndex()].path);
      } else if (this.searchText().trim()) {
        this.addTag(this.searchText());
      }
    } else if (event.key === 'Backspace' && this.searchText() === '' && this.value().length > 0) {
      this.removeTag(this.value()[this.value().length - 1]);
    } else if (event.key === 'ArrowDown' && this.showSuggestions()) {
      event.preventDefault();
      this.selectedIndex.update((i) => (i < filtered.length - 1 ? i + 1 : i));
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
