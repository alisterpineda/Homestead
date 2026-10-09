import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, of, catchError } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown, lucideCheck, lucidePlus } from '@ng-icons/lucide';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { SearchInputComponent } from '@/shared/components/search-input/search-input.component';
import { PickerTriggerDirective } from '@/shared/directives/picker-trigger.directive';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { FinancesCategoryApiService } from '@/api/services/finances-category-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { CategoryResponse } from '@/api/generated/models/category-response';

export type CategoryDirection = 'Expense' | 'Income';

/**
 * Direction-filtered category chooser: the whole tree of one direction rendered as an indented flat
 * list (depth-indented), with search and (when {@link allowCreate}) find-or-create. System Uncategorised
 * buckets are never selectable — the clear row (label {@link clearLabel}) sets the value to null instead.
 */
@Component({
  selector: 'app-category-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmPopoverImports, SearchInputComponent, PickerTriggerDirective, HlmSpinner],
  providers: [provideIcons({ lucideChevronDown, lucideCheck, lucidePlus })],
  template: `
    <div hlmPopover [state]="popoverState()" (stateChanged)="popoverState.set($event)">
      <button
        hlmPopoverTrigger
        appPickerTrigger
        type="button"
        [disabled]="disabled()"
        [attr.aria-label]="ariaLabel() || 'Select category'"
      >
        @if (selected(); as sel) {
          <span class="min-w-0 truncate">{{ sel.path }}</span>
        } @else {
          <span class="min-w-0 truncate text-muted-foreground">{{ placeholder() }}</span>
        }
        <ng-icon name="lucideChevronDown" class="text-[length:--spacing(4)] shrink-0 text-muted-foreground" />
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-[320px] p-0" align="start">
        <div class="p-2">
          <app-search-input [(value)]="searchTerm" placeholder="Search or add a category..." ariaLabel="Search categories" />
        </div>
        <div class="max-h-[280px] overflow-y-auto p-1">
          <button
            type="button"
            class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
            (click)="onClear()"
          >
            <span class="size-4 shrink-0 flex items-center justify-center">
              @if (value() === null) {
                <ng-icon name="lucideCheck" class="text-[length:--spacing(4)]" />
              }
            </span>
            <span class="text-muted-foreground">{{ clearLabel() }}</span>
          </button>

          @if (loading()) {
            <div class="flex justify-center py-6">
              <hlm-spinner />
            </div>
          } @else {
            @for (c of results(); track c.id) {
              <button
                type="button"
                class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
                (click)="onSelect(c)"
              >
                <span class="size-4 shrink-0 flex items-center justify-center">
                  @if (c.id === value()) {
                    <ng-icon name="lucideCheck" class="text-[length:--spacing(4)]" />
                  }
                </span>
                <span class="shrink-0" [style.width.px]="c.depth * 14"></span>
                <span class="truncate">{{ c.name }}</span>
              </button>
            }
            @if (showCreateRow()) {
              <button
                type="button"
                class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
                [disabled]="creating()"
                (click)="onCreate()"
              >
                <span class="size-4 shrink-0 flex items-center justify-center">
                  @if (creating()) {
                    <hlm-spinner class="size-3" />
                  } @else {
                    <ng-icon name="lucidePlus" class="text-[length:--spacing(4)]" />
                  }
                </span>
                <span class="truncate">Create "{{ searchTerm().trim() }}"</span>
              </button>
            } @else if (results().length === 0) {
              <p class="py-4 text-center text-sm text-muted-foreground">No categories found.</p>
            }
          }
        </div>
      </div>
    </div>
  `,
})
export class CategoryPickerComponent {
  /** The selected category id, or null for Uncategorised / root. Seeded by the parent form. */
  value = input<string | null>(null);
  /** Which direction's categories to show — Expense categories for an Expense flow, etc. */
  direction = input<CategoryDirection>('Expense');
  disabled = input(false);
  placeholder = input('Uncategorised');
  clearLabel = input('Uncategorised');
  ariaLabel = input<string | null>(null);
  /** When false, the "Create 'X'" row is hidden — a pure chooser (the parent pickers use this). */
  allowCreate = input(true);
  /** Category ids to omit — the edit sheet excludes the category itself and its descendants. */
  excludeIds = input<readonly string[]>([]);

  valueChange = output<string | null>();

  popoverState = signal<'open' | 'closed'>('closed');
  searchTerm = signal('');
  loading = signal(false);
  creating = signal(false);

  private categoryApi = inject(FinancesCategoryApiService);
  private errorHandler = inject(ErrorHandlerService);

  // Categories inline-created through this picker — kept locally so the trigger/list can show them
  // before the parent refetches. Filtered by direction so a direction switch drops stale ones.
  private localCreated = signal<CategoryResponse[]>([]);

  private response = toSignal(
    toObservable(this.direction).pipe(
      switchMap(dir => {
        this.loading.set(true);
        return this.categoryApi.getCategories({ Type: dir }).pipe(
          map(items => items ?? []),
          catchError(() => of([] as CategoryResponse[])),
        );
      }),
      map(items => {
        this.loading.set(false);
        return items;
      }),
    ),
    { initialValue: [] as CategoryResponse[] },
  );

  private all = computed(() => {
    const base = this.response();
    const extra = this.localCreated().filter(
      e => e.type === this.direction() && !base.some(b => b.id === e.id),
    );
    return [...base, ...extra];
  });

  // Selectable = user categories of this direction, minus any excluded ids. System buckets never appear.
  // The direction filter is defence-in-depth (the backend already filters by Type): it also drops the
  // stale wrong-direction results that linger between a direction switch and its refetch resolving.
  private selectable = computed(() => {
    const excl = new Set(this.excludeIds());
    const dir = this.direction();
    return this.all().filter(c => !c.isSystem && c.type === dir && !excl.has(c.id));
  });

  results = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const items = this.selectable();
    if (!term) return items;
    return items.filter(c => c.name.toLowerCase().includes(term) || c.path.toLowerCase().includes(term));
  });

  selected = computed(() => this.all().find(c => c.id === this.value()) ?? null);

  showCreateRow = computed(() => {
    if (!this.allowCreate()) return false;
    const text = this.searchTerm().trim();
    if (!text || this.loading()) return false;
    const lower = text.toLowerCase();
    return !this.selectable().some(c => c.name.toLowerCase() === lower);
  });

  onSelect(category: CategoryResponse): void {
    this.valueChange.emit(category.id);
    this.searchTerm.set('');
    this.popoverState.set('closed');
  }

  onClear(): void {
    this.valueChange.emit(null);
    this.searchTerm.set('');
    this.popoverState.set('closed');
  }

  onCreate(): void {
    const name = this.searchTerm().trim();
    if (!name || this.creating()) return;

    this.creating.set(true);
    this.categoryApi.createCategory({ body: { name, type: this.direction(), parentId: null } }).subscribe({
      next: category => {
        this.creating.set(false);
        this.localCreated.update(list => [...list, category]);
        this.onSelect(category);
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.errorHandler.handleError(err, 'Failed to create category.');
      },
    });
  }
}
