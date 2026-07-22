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
import { lucideChevronDown, lucideCheck, lucideBox, lucideCpu } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { SearchInputComponent } from '@/shared/components/search-input/search-input.component';
import { PickerTriggerDirective } from '@/shared/directives/picker-trigger.directive';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { InventoryAreaApiService } from '@/api/services/inventory-area-api.service';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import { isTerminalStatus } from '../item-status-meta';
import { orderAreaTree, type AreaTreeNode } from '../area-tree';
import type { AreaResponse } from '@/api/generated/models/area-response';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

/** A picked location: nowhere, an area, or a container item. */
export type ItemLocationValue =
  | { kind: 'unplaced' }
  | { kind: 'area'; id: string }
  | { kind: 'container'; id: string };

interface LoadedOptions {
  areas: AreaResponse[];
  containers: DurableResponse[];
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
  imports: [NgIcon, HlmIconImports, HlmPopoverImports, SearchInputComponent, HlmSpinner, PickerTriggerDirective],
  providers: [provideIcons({ lucideChevronDown, lucideCheck, lucideBox, lucideCpu })],
  template: `
    <div hlmPopover [state]="popoverState()" (stateChanged)="popoverState.set($event)">
      <button
        hlmPopoverTrigger
        type="button"
        [disabled]="disabled()"
        [attr.aria-label]="ariaLabel() || 'Select location'"
        appPickerTrigger
      >
        @if (displayLabel(); as label) {
          <span class="min-w-0 truncate">{{ label }}</span>
        } @else {
          <span class="min-w-0 truncate text-muted-foreground italic">{{ allowUnplaced() ? 'Unplaced' : (placeholder() ?? 'Select location') }}</span>
        }
        <ng-icon hlm name="lucideChevronDown" size="sm" class="shrink-0 text-muted-foreground" />
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-[340px] p-0" align="start">
        <div class="p-2">
          <app-search-input [(value)]="searchTerm" placeholder="Search areas, containers, devices…" ariaLabel="Search areas, containers, devices" />
        </div>
        <div class="max-h-[320px] overflow-y-auto p-1">
          @if (loading()) {
            <div class="flex justify-center py-4"><hlm-spinner /></div>
          } @else {
            @if (allowUnplaced()) {
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
            }

            @if (filteredAreas().length > 0) {
              <span class="block px-2 pb-1 pt-2 text-xs font-medium text-muted-foreground">Areas</span>
              @for (node of filteredAreas(); track node.area.id) {
                <button
                  type="button"
                  class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                  (click)="onSelectArea(node.area)"
                >
                  <span class="truncate" [style.marginInlineStart.px]="node.depth * 14">{{ node.area.name }}</span>
                  @if (isSelectedArea(node.area.id)) {
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

            @if (filteredAssemblies().length > 0) {
              <span class="block px-2 pb-1 pt-2 text-xs font-medium text-muted-foreground">Part of</span>
              @for (assembly of filteredAssemblies(); track assembly.id) {
                <button
                  type="button"
                  class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                  (click)="onSelectContainer(assembly)"
                >
                  <span class="flex min-w-0 items-center gap-2">
                    <ng-icon hlm name="lucideCpu" size="sm" class="shrink-0 text-muted-foreground" />
                    <span class="min-w-0">
                      <span class="block truncate">{{ assembly.name }}</span>
                      @if (containerSubtitle(assembly); as subtitle) {
                        <span class="block truncate text-xs text-muted-foreground">{{ subtitle }}</span>
                      }
                    </span>
                  </span>
                  @if (isSelectedContainer(assembly.id)) {
                    <ng-icon hlm name="lucideCheck" size="sm" />
                  }
                </button>
              }
            }

            @if (filteredAreas().length === 0 && filteredContainers().length === 0 && filteredAssemblies().length === 0) {
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
  /** Offer the "Unplaced" option. Stock locations require a real place, so they pass false. */
  allowUnplaced = input(true);
  /** Trigger text when nothing is selected and Unplaced is not offered. */
  placeholder = input<string | null>(null);

  valueChange = output<ItemLocationValue>();

  private areaApi = inject(InventoryAreaApiService);
  private durableApi = inject(InventoryDurableApiService);

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
          containers: this.durableApi
            .getDurables({
              Page: 1,
              PageSize: 200,
              IncludeInactive: true,
            })
            .pipe(
              map(r => (r.items ?? []) as DurableResponse[]),
              catchError(() => of<DurableResponse[]>([])),
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

  // Areas indented by depth so nesting is visible; all areas stay selectable ("any level"
  // placement — no subtree exclusion for areas, unlike containers).
  protected filteredAreas = computed<AreaTreeNode[]>(() => {
    const tree = orderAreaTree(this.areas());
    const term = this.searchTerm().trim().toLowerCase();
    return term ? tree.filter(n => n.area.name.toLowerCase().includes(term)) : tree;
  });

  // Storage places (drillable containers) and assemblies (non-containers that hold components) render
  // as two distinct groups; both still emit the same { kind: 'container', id } value (the server
  // derives the relationship from the target's IsContainer). A regular leaf item (neither) is offered
  // in neither group.
  protected filteredContainers = computed(() => this.filteredHolders(c => c.isContainer === true));
  protected filteredAssemblies = computed(() =>
    this.filteredHolders(c => c.isContainer !== true && c.hasComponents === true));

  private filteredHolders(predicate: (c: DurableResponse) => boolean): DurableResponse[] {
    const term = this.searchTerm().trim().toLowerCase();
    const excluded = this.excludedIds();
    // Display live holders only; the exclusion walk above still crosses terminal ones.
    const live = this.allContainers().filter(
      c => predicate(c) && !excluded.has(c.id) && !isTerminalStatus(c.status),
    );
    return term ? live.filter(c => c.name.toLowerCase().includes(term)) : live;
  }

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

  protected containerSubtitle(container: DurableResponse): string {
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

  protected onSelectUnplaced(): void {
    this.emit({ kind: 'unplaced' }, 'Unplaced');
  }

  protected onSelectArea(area: AreaResponse): void {
    this.emit({ kind: 'area', id: area.id }, area.name);
  }

  protected onSelectContainer(container: DurableResponse): void {
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
