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
import { HttpClient } from '@angular/common/http';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, debounceTime, of, map, tap, catchError } from 'rxjs';
import { PartyBadgeComponent } from '../party-badge/party-badge.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getParties } from '@/api/generated/fn/documents/get-parties';
import { createParty } from '@/api/generated/fn/documents/create-party';
import type { GetPartiesResponse } from '@/api/generated/models/get-parties-response';
import { CREATE_OPTION_ID } from '../bulk-edit-shared';

export interface PartyOption {
  id: string;
  name: string;
}

@Component({
  selector: 'app-party-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PartyBadgeComponent],
  host: {
    '(document:mousedown)': 'onClickOutside($event)',
  },
  template: `
    <div class="relative" #container>
      <div
        class="flex flex-wrap items-center gap-1.5 rounded-md border border-input bg-background px-2 py-1.5 transition-colors focus-within:border-ring focus-within:ring-2 focus-within:ring-ring/20"
      >
        @for (party of value(); track party.id) {
          <app-party-badge [name]="party.name" [removable]="true" (removed)="removeParty(party.id)" />
        }
        <input
          #inputEl
          [value]="searchText()"
          (input)="onSearchInput($event)"
          (focus)="onFocus()"
          (keydown)="onKeyDown($event)"
          [placeholder]="value().length === 0 ? 'Add parties...' : ''"
          class="min-w-[80px] flex-1 bg-transparent text-sm text-foreground placeholder:text-muted-foreground outline-none"
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
                'w-full px-3 py-2 text-left text-sm transition-colors cursor-pointer truncate ' +
                (i === selectedIndex()
                  ? 'bg-accent text-accent-foreground'
                  : 'text-foreground hover:bg-accent') +
                (i === 0 ? ' rounded-t-md' : '') +
                (i === allOptions().length - 1 ? ' rounded-b-md' : '')
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
  `,
})
export class PartyInputComponent {
  protected readonly createOptionId = CREATE_OPTION_ID;

  value = input.required<PartyOption[]>();
  valueChange = output<PartyOption[]>();

  searchText = signal('');
  showSuggestions = signal(false);
  selectedIndex = signal(0);
  creating = signal(false);
  loading = signal(false);

  inputEl = viewChild<ElementRef<HTMLInputElement>>('inputEl');
  container = viewChild<ElementRef<HTMLDivElement>>('container');

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  private suggestions = toSignal(
    toObservable(this.searchText).pipe(
      debounceTime(200),
      switchMap((text) => {
        if (!text) {
          this.loading.set(false);
          return of([]);
        }
        return getParties(this.http, this.apiConfig.rootUrl, {
          Search: text,
          Page: 1,
          PageSize: 10,
        }).pipe(
          map((r) => r.body.items as GetPartiesResponse[]),
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

  private filteredSuggestions = computed(() => {
    const currentIds = new Set(this.value().map((p) => p.id));
    return (this.suggestions() ?? []).filter((s) => !currentIds.has(s.id));
  });

  allOptions = computed(() => {
    const filtered = this.filteredSuggestions();
    const text = this.searchText().trim();
    const options: PartyOption[] = filtered.map((s) => ({ id: s.id, name: s.name }));

    const lowerText = text.toLowerCase();
    if (text && !this.loading() && !filtered.some((s) => s.name.toLowerCase() === lowerText)) {
      options.push({ id: CREATE_OPTION_ID, name: text });
    }

    return options;
  });

  constructor() {
    effect(() => {
      this.searchText();
      if (untracked(this.selectedIndex) !== 0) this.selectedIndex.set(0);
    });
  }

  selectOption(option: PartyOption): void {
    if (option.id === CREATE_OPTION_ID) {
      this.createAndAdd(option.name);
    } else {
      this.addParty(option);
    }
  }

  addParty(party: PartyOption): void {
    if (!this.value().some((p) => p.id === party.id)) {
      this.valueChange.emit([...this.value(), party]);
    }
    this.searchText.set('');
    this.showSuggestions.set(false);
    this.inputEl()?.nativeElement.focus();
  }

  removeParty(id: string): void {
    this.valueChange.emit(this.value().filter((p) => p.id !== id));
  }

  private createAndAdd(name: string): void {
    if (this.creating()) return;
    this.creating.set(true);
    createParty(this.http, this.apiConfig.rootUrl, {
      body: { name },
    }).subscribe({
      next: (r) => {
        this.creating.set(false);
        this.addParty({ id: r.body.id, name: r.body.name });
      },
      error: () => {
        this.creating.set(false);
      },
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
      this.removeParty(this.value()[this.value().length - 1].id);
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
