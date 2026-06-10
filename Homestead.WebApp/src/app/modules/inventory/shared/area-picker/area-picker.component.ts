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
import { switchMap, of, map, catchError } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown, lucideSearch, lucideCheck } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { InventoryAreaApiService } from '@/api/services/inventory-area-api.service';
import type { AreaResponse } from '@/api/generated/models/area-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

/**
 * Picks an area for placing an item, with a first-class "(Unplaced)" null option. Self-contained:
 * depends only on the root-provided {@link InventoryAreaApiService}, so it is reusable cross-feature.
 */
@Component({
  selector: 'app-area-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmPopoverImports, HlmInput, HlmSpinner],
  providers: [provideIcons({ lucideChevronDown, lucideSearch, lucideCheck })],
  template: `
    <div hlmPopover [state]="popoverState()" (stateChanged)="popoverState.set($event)">
      <button
        hlmPopoverTrigger
        type="button"
        [disabled]="disabled()"
        [attr.aria-label]="ariaLabel() || 'Select area'"
        class="flex h-9 w-full items-center justify-between rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-xs transition-colors disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        @if (value()) {
          <span class="truncate">{{ selectedName() }}</span>
        } @else {
          <span class="truncate text-muted-foreground italic">Unplaced</span>
        }
        <ng-icon hlm name="lucideChevronDown" size="sm" class="text-muted-foreground" />
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-[320px] p-0" align="start">
        <div class="relative p-2">
          <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-4 top-1/2 -translate-y-1/2 text-muted-foreground" />
          <input
            hlmInput
            class="h-8 w-full pl-8"
            placeholder="Search areas..."
            [value]="searchTerm()"
            (input)="searchTerm.set(asValue($event))"
            aria-label="Search areas"
          />
        </div>
        <div class="max-h-[280px] overflow-y-auto p-1">
          @if (loading()) {
            <div class="flex justify-center py-4"><hlm-spinner class="size-4" /></div>
          } @else {
            <button
              type="button"
              class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
              (click)="onSelect(null)"
            >
              <span class="truncate italic text-muted-foreground">Unplaced</span>
              @if (!value()) {
                <ng-icon hlm name="lucideCheck" size="sm" />
              }
            </button>
            @for (area of filtered(); track area.id) {
              <button
                type="button"
                class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                (click)="onSelect(area)"
              >
                <span class="truncate">{{ area.name }}</span>
                @if (value() === area.id) {
                  <ng-icon hlm name="lucideCheck" size="sm" />
                }
              </button>
            } @empty {
              <p class="px-2 py-4 text-center text-sm text-muted-foreground">No areas found.</p>
            }
          }
        </div>
      </div>
    </div>
  `,
})
export class AreaPickerComponent {
  value = input<string | null>(null);
  disabled = input(false);
  ariaLabel = input<string | null>(null);
  /** Known name of the selected area, shown before the list loads. */
  selectedAreaName = input<string | null>(null);

  valueChange = output<string | null>();

  private areaApi = inject(InventoryAreaApiService);

  protected popoverState = signal<BrnDialogState>('closed');
  protected searchTerm = signal('');
  protected loading = signal(false);

  // Remembers the last picked area so the trigger keeps showing its name after the popover closes
  // (the lazy `areas` list empties on close, so it can't be relied on for display).
  private picked = signal<AreaResponse | null>(null);

  private areas = toSignal(
    toObservable(this.popoverState).pipe(
      switchMap(state => {
        if (state !== 'open') return of<AreaResponse[]>([]);
        this.loading.set(true);
        return this.areaApi.getAreas({ Page: 1, PageSize: 200 }).pipe(
          map(r => {
            this.loading.set(false);
            return r.items ?? [];
          }),
          catchError(() => {
            this.loading.set(false);
            return of<AreaResponse[]>([]);
          }),
        );
      }),
    ),
    { initialValue: [] as AreaResponse[] },
  );

  protected filtered = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const all = this.areas();
    return term ? all.filter(a => a.name.toLowerCase().includes(term)) : all;
  });

  protected selectedName = computed(() => {
    const id = this.value();
    if (!id) return '';
    const p = this.picked();
    if (p && p.id === id) return p.name;
    return this.areas().find(a => a.id === id)?.name ?? this.selectedAreaName() ?? '';
  });

  protected asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected onSelect(area: AreaResponse | null): void {
    this.picked.set(area);
    this.valueChange.emit(area?.id ?? null);
    this.popoverState.set('closed');
  }
}
