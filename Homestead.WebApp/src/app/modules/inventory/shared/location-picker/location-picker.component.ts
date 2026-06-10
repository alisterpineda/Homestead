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
import { switchMap, of, map, catchError, forkJoin } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown, lucideSearch, lucideCheck, lucideBox } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { InventoryAreaApiService } from '@/api/services/inventory-area-api.service';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { isTerminalStatus } from '../item-status-meta';
import type { AreaResponse } from '@/api/generated/models/area-response';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

/** A picked location: nowhere, an area, or a container item. */
export type ItemLocationValue =
  | { kind: 'unplaced' }
  | { kind: 'area'; id: string }
  | { kind: 'container'; id: string };

interface LoadedOptions {
  areas: AreaResponse[];
  containers: ItemResponse[];
}

const EMPTY_OPTIONS: LoadedOptions = { areas: [], containers: [] };

/**
 * Picks where an item lives: "(Unplaced)", an area, or a container item — emitting a discriminated
 * {@link ItemLocationValue}. Lazy-loads areas + live containers on open. Excludes the edited item
 * and its own subtree (the server's cycle check stays authoritative). Mirrors the area picker.
 */
@Component({
  selector: 'app-location-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmPopoverImports, HlmInput, HlmSpinner],
  providers: [provideIcons({ lucideChevronDown, lucideSearch, lucideCheck, lucideBox })],
  template: `
    <div hlmPopover [state]="popoverState()" (stateChanged)="popoverState.set($event)">
      <button
        hlmPopoverTrigger
        type="button"
        [disabled]="disabled()"
        [attr.aria-label]="ariaLabel() || 'Select location'"
        class="flex h-9 w-full items-center justify-between rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-xs transition-colors disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        @if (displayLabel(); as label) {
          <span class="truncate">{{ label }}</span>
        } @else {
          <span class="truncate text-muted-foreground italic">Unplaced</span>
        }
        <ng-icon hlm name="lucideChevronDown" size="sm" class="text-muted-foreground" />
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-[340px] p-0" align="start">
        <div class="relative p-2">
          <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-4 top-1/2 -translate-y-1/2 text-muted-foreground" />
          <input
            hlmInput
            class="h-8 w-full pl-8"
            placeholder="Search areas and containers..."
            [value]="searchTerm()"
            (input)="searchTerm.set(asValue($event))"
            aria-label="Search areas and containers"
          />
        </div>
        <div class="max-h-[320px] overflow-y-auto p-1">
          @if (loading()) {
            <div class="flex justify-center py-4"><hlm-spinner class="size-4" /></div>
          } @else {
            <button
              type="button"
              class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
              (click)="onSelectUnplaced()"
            >
              <span class="truncate italic text-muted-foreground">Unplaced</span>
              @if (value().kind === 'unplaced') {
                <ng-icon hlm name="lucideCheck" size="sm" />
              }
            </button>

            @if (filteredAreas().length > 0) {
              <span class="block px-2 pb-1 pt-2 text-xs font-medium text-muted-foreground">Areas</span>
              @for (area of filteredAreas(); track area.id) {
                <button
                  type="button"
                  class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                  (click)="onSelectArea(area)"
                >
                  <span class="truncate">{{ area.name }}</span>
                  @if (isSelectedArea(area.id)) {
                    <ng-icon hlm name="lucideCheck" size="sm" />
                  }
                </button>
              }
            }

            @if (filteredContainers().length > 0) {
              <span class="block px-2 pb-1 pt-2 text-xs font-medium text-muted-foreground">Containers</span>
              @for (container of filteredContainers(); track container.id) {
                <button
                  type="button"
                  class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                  (click)="onSelectContainer(container)"
                >
                  <span class="flex min-w-0 items-center gap-2">
                    <ng-icon hlm name="lucideBox" size="sm" class="shrink-0 text-muted-foreground" />
                    <span class="min-w-0">
                      <span class="block truncate">{{ container.name }}</span>
                      @if (containerSubtitle(container); as subtitle) {
                        <span class="block truncate text-xs text-muted-foreground">{{ subtitle }}</span>
                      }
                    </span>
                  </span>
                  @if (isSelectedContainer(container.id)) {
                    <ng-icon hlm name="lucideCheck" size="sm" />
                  }
                </button>
              }
            }

            @if (filteredAreas().length === 0 && filteredContainers().length === 0) {
              <p class="px-2 py-4 text-center text-sm text-muted-foreground">No locations found.</p>
            }
          }
        </div>
      </div>
    </div>
  `,
})
export class LocationPickerComponent {
  value = input<ItemLocationValue>({ kind: 'unplaced' });
  disabled = input(false);
  ariaLabel = input<string | null>(null);
  /** Known label of the current selection, shown before the list loads. */
  selectedLabel = input<string | null>(null);
  /** Edited item to exclude (with its whole subtree) from the container options. */
  excludeItemId = input<string | null>(null);

  valueChange = output<ItemLocationValue>();

  private areaApi = inject(InventoryAreaApiService);
  private itemApi = inject(InventoryItemApiService);

  protected popoverState = signal<BrnDialogState>('closed');
  protected searchTerm = signal('');
  protected loading = signal(false);

  // Remembers the last picked option so the trigger keeps its label after the popover closes
  // (the lazy lists empty on close, so they can't be relied on for display).
  private picked = signal<{ value: ItemLocationValue; label: string } | null>(null);

  private loaded = toSignal(
    toObservable(this.popoverState).pipe(
      switchMap(state => {
        if (state !== 'open') return of(EMPTY_OPTIONS);
        this.loading.set(true);
        return forkJoin({
          areas: this.areaApi.getAreas({ Page: 1, PageSize: 200 }).pipe(
            map(r => r.items ?? []),
            catchError(() => of<AreaResponse[]>([])),
          ),
          containers: this.itemApi
            .getItems({ Page: 1, PageSize: 200, IsContainer: true, IncludeInactive: true })
            .pipe(
              map(r => (r.items ?? []) as ItemResponse[]),
              catchError(() => of<ItemResponse[]>([])),
            ),
        }).pipe(
          map(result => {
            this.loading.set(false);
            return result;
          }),
          catchError(() => {
            this.loading.set(false);
            return of(EMPTY_OPTIONS);
          }),
        );
      }),
    ),
    { initialValue: EMPTY_OPTIONS },
  );

  protected areas = computed(() => this.loaded().areas);
  private allContainers = computed(() => this.loaded().containers);

  // The edited item plus its whole container subtree — never offerable as a new parent. Computed
  // over the FULL set (incl. terminal containers) so the walk can't be severed by a sold ancestor.
  private excludedIds = computed(() => {
    const id = this.excludeItemId();
    const excluded = new Set<string>();
    if (!id) return excluded;
    excluded.add(id);

    const childrenByParent = new Map<string, string[]>();
    for (const c of this.allContainers()) {
      if (c.parentItemId) {
        const siblings = childrenByParent.get(c.parentItemId) ?? [];
        siblings.push(c.id);
        childrenByParent.set(c.parentItemId, siblings);
      }
    }

    const stack = [id];
    while (stack.length > 0) {
      const current = stack.pop()!;
      for (const child of childrenByParent.get(current) ?? []) {
        if (!excluded.has(child)) {
          excluded.add(child);
          stack.push(child);
        }
      }
    }
    return excluded;
  });

  protected filteredAreas = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const all = this.areas();
    return term ? all.filter(a => a.name.toLowerCase().includes(term)) : all;
  });

  protected filteredContainers = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const excluded = this.excludedIds();
    // Display live containers only; the exclusion walk above still crosses terminal ones.
    const live = this.allContainers().filter(c => !excluded.has(c.id) && !isTerminalStatus(c.status));
    return term ? live.filter(c => c.name.toLowerCase().includes(term)) : live;
  });

  protected displayLabel = computed<string | null>(() => {
    const v = this.value();
    if (v.kind === 'unplaced') return null;

    const p = this.picked();
    if (p && sameValue(p.value, v)) return p.label;

    if (v.kind === 'area') {
      return this.areas().find(a => a.id === v.id)?.name ?? this.selectedLabel() ?? '';
    }
    return this.allContainers().find(c => c.id === v.id)?.name ?? this.selectedLabel() ?? '';
  });

  protected containerSubtitle(container: ItemResponse): string {
    const names = (container.path ?? []).map(s => s.name);
    if (names.length === 0) return '';
    return (container.resolvedAreaId == null ? ['Unplaced', ...names] : names).join(' › ');
  }

  protected isSelectedArea(id: string): boolean {
    const v = this.value();
    return v.kind === 'area' && v.id === id;
  }

  protected isSelectedContainer(id: string): boolean {
    const v = this.value();
    return v.kind === 'container' && v.id === id;
  }

  protected asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected onSelectUnplaced(): void {
    this.emit({ kind: 'unplaced' }, 'Unplaced');
  }

  protected onSelectArea(area: AreaResponse): void {
    this.emit({ kind: 'area', id: area.id }, area.name);
  }

  protected onSelectContainer(container: ItemResponse): void {
    this.emit({ kind: 'container', id: container.id }, container.name);
  }

  private emit(value: ItemLocationValue, label: string): void {
    this.picked.set({ value, label });
    this.valueChange.emit(value);
    this.popoverState.set('closed');
  }
}

function sameValue(a: ItemLocationValue, b: ItemLocationValue): boolean {
  if (a.kind !== b.kind) return false;
  if (a.kind === 'unplaced' || b.kind === 'unplaced') return true;
  return a.id === (b as { id: string }).id;
}
