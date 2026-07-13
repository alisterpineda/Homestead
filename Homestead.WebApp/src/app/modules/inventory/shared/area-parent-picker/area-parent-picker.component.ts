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
import { lucideChevronDown, lucideCheck, lucideMapPin } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { SearchInputComponent } from '@/shared/components/search-input/search-input.component';
import { PickerTriggerDirective } from '@/shared/directives/picker-trigger.directive';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { InventoryAreaApiService } from '@/api/services/inventory-area-api.service';
import { orderAreaTree, buildDescendantMap, type AreaTreeNode } from '../area-tree';
import type { AreaResponse } from '@/api/generated/models/area-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

/**
 * Picks an area's parent: "None (top-level)" or another area, emitting the parent area id (or
 * null). Lazy-loads areas on open and renders them indented by depth so the hierarchy is visible.
 * When editing, {@link excludeSubtreeOf} removes the edited area and all its descendants from the
 * options (a parent can't be inside its own subtree); the server's cycle check stays authoritative.
 */
@Component({
  selector: 'app-area-parent-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmPopoverImports, SearchInputComponent, HlmSpinner, PickerTriggerDirective],
  providers: [provideIcons({ lucideChevronDown, lucideCheck, lucideMapPin })],
  template: `
    <div hlmPopover [state]="popoverState()" (stateChanged)="popoverState.set($event)">
      <button
        hlmPopoverTrigger
        type="button"
        [disabled]="disabled()"
        [attr.aria-label]="ariaLabel() || 'Select parent area'"
        appPickerTrigger
      >
        @if (displayLabel(); as label) {
          <span class="min-w-0 truncate">{{ label }}</span>
        } @else {
          <span class="min-w-0 truncate text-muted-foreground italic">None (top-level)</span>
        }
        <ng-icon hlm name="lucideChevronDown" size="sm" class="shrink-0 text-muted-foreground" />
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-[340px] p-0" align="start">
        <div class="p-2">
          <app-search-input [(value)]="searchTerm" placeholder="Search areas..." ariaLabel="Search areas" />
        </div>
        <div class="max-h-[320px] overflow-y-auto p-1">
          @if (loading()) {
            <div class="flex justify-center py-4"><hlm-spinner /></div>
          } @else {
            <button
              type="button"
              class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
              (click)="onSelect(null, 'None (top-level)')"
            >
              <span class="truncate italic text-muted-foreground">None (top-level)</span>
              @if (value() == null) {
                <ng-icon hlm name="lucideCheck" size="sm" />
              }
            </button>

            @for (node of filteredAreas(); track node.area.id) {
              <button
                type="button"
                class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                (click)="onSelect(node.area.id, node.area.name)"
              >
                <span class="flex min-w-0 items-center gap-2">
                  <ng-icon hlm name="lucideMapPin" size="sm" class="shrink-0 text-muted-foreground" />
                  <span class="truncate" [style.marginInlineStart.px]="node.depth * 14">{{ node.area.name }}</span>
                </span>
                @if (value() === node.area.id) {
                  <ng-icon hlm name="lucideCheck" size="sm" />
                }
              </button>
            }

            @if (filteredAreas().length === 0) {
              <p class="px-2 py-4 text-center text-sm text-muted-foreground">No areas found.</p>
            }
          }
        </div>
      </div>
    </div>
  `,
})
export class AreaParentPickerComponent {
  /** Selected parent area id, or null for top-level. */
  value = input<string | null>(null);
  disabled = input(false);
  ariaLabel = input<string | null>(null);
  /** Known name of the current parent, shown before the list loads. */
  selectedLabel = input<string | null>(null);
  /** Edited area to exclude (with its whole subtree) — a parent can't be inside its own descendants. */
  excludeSubtreeOf = input<string | null>(null);

  valueChange = output<string | null>();

  private areaApi = inject(InventoryAreaApiService);

  protected popoverState = signal<BrnDialogState>('closed');
  protected searchTerm = signal('');
  protected loading = signal(false);

  // Remembers the last picked option so the trigger keeps its label after the popover closes (the
  // lazy list empties on close, so it can't be relied on for display).
  private picked = signal<{ id: string | null; label: string } | null>(null);

  private loaded = toSignal(
    toObservable(this.popoverState).pipe(
      switchMap(state => {
        if (state !== 'open') return of<AreaResponse[]>([]);
        this.loading.set(true);
        return this.areaApi.getAreas({ Page: 1, PageSize: 200 }).pipe(
          map(r => {
            this.loading.set(false);
            return (r.items ?? []) as AreaResponse[];
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

  // The edited area plus its whole subtree — never offerable as a parent.
  private excludedIds = computed(() => {
    const id = this.excludeSubtreeOf();
    if (!id) return new Set<string>();
    return buildDescendantMap(this.loaded()).get(id) ?? new Set([id]);
  });

  protected filteredAreas = computed<AreaTreeNode[]>(() => {
    const excluded = this.excludedIds();
    const selectable = this.loaded().filter(a => !excluded.has(a.id));
    const tree = orderAreaTree(selectable);
    const term = this.searchTerm().trim().toLowerCase();
    return term ? tree.filter(n => n.area.name.toLowerCase().includes(term)) : tree;
  });

  protected displayLabel = computed<string | null>(() => {
    const id = this.value();
    if (id == null) return null;

    const p = this.picked();
    if (p && p.id === id) return p.label;

    return this.loaded().find(a => a.id === id)?.name ?? this.selectedLabel() ?? '';
  });

  protected onSelect(id: string | null, label: string): void {
    this.picked.set({ id, label });
    this.valueChange.emit(id);
    this.popoverState.set('closed');
  }
}
