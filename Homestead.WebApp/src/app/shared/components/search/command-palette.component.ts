import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged, of, switchMap, map, tap } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSearch, lucidePackage, lucideBoxes } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { CommandFilter } from '@spartan-ng/brain/command';
import { HlmCommandImports } from '@/shared/components/spartan/command';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { SearchApiService } from '@/api/services';
import type { SearchResponse } from '@/api/generated/models/search-response';
import type { SearchGroup } from '@/api/generated/models/search-group';
import type { SearchHit } from '@/api/generated/models/search-hit';
import { GlobalSearchService } from '@/shared/services/global-search.service';
import { SearchPresenterRegistry } from './search-presenters';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';

/**
 * Global command palette: a single search box (in the shell header / ⌘K) that searches across
 * modules and navigates to the chosen result. Results are grouped per source; rendering and routing
 * for each source come from the {@link SearchPresenterRegistry}, so new modules are additive.
 *
 * Client-side filtering is disabled ({@link alwaysVisible}) — the backend already filtered and
 * fuzzy-matched, and a literal client filter would hide typo matches (e.g. "battry" → "Battery").
 * The brain command primitive still provides keyboard navigation over the rendered results.
 */
@Component({
  selector: 'app-command-palette',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmCommandImports, HlmSpinner, NgIcon],
  providers: [provideIcons({ lucideSearch, lucidePackage, lucideBoxes })],
  template: `
    <hlm-command-dialog
      title="Search"
      description="Search across Homestead and jump to a result"
      dialogContentClass="w-[min(36rem,calc(100vw-2rem))] p-0"
      [state]="globalSearch.open() ? 'open' : 'closed'"
      (stateChange)="onStateChange($event)"
    >
      <hlm-command
        [filter]="alwaysVisible"
        [search]="searchInput()"
        (searchChange)="searchInput.set($event)"
      >
        <hlm-command-input placeholder="Search…" />

        <hlm-command-list>
          @switch (state()) {
            @case ('hint') {
              <div class="py-6 text-center text-sm text-muted-foreground">Type to search…</div>
            }
            @case ('loading') {
              <div class="flex items-center justify-center py-6">
                <hlm-spinner class="size-5" />
              </div>
            }
            @case ('error') {
              <div hlmCommandEmpty>Something went wrong. Try again.</div>
            }
            @case ('empty') {
              <div hlmCommandEmpty>No results found.</div>
            }
            @case ('results') {
              @for (group of groups(); track group.source) {
                <hlm-command-group>
                  <span hlmCommandGroupLabel>{{ group.label }}</span>

                  @for (hit of group.results; track hit.id) {
                    <button hlmCommandItem [value]="hit.source + ':' + hit.id" (selected)="onSelect(hit)">
                      <ng-icon [name]="iconFor(hit)" />
                      <span class="flex min-w-0 flex-1 flex-col text-left">
                        <span class="truncate">{{ hit.title }}</span>
                        @if (hit.subtitle) {
                          <span class="truncate text-xs text-muted-foreground">{{ hit.subtitle }}</span>
                        }
                      </span>
                      @if (hit.badge) {
                        <span class="ml-auto shrink-0 rounded-md bg-muted px-1.5 py-0.5 text-xs text-muted-foreground">
                          {{ hit.badge }}
                        </span>
                      }
                    </button>
                  }

                  @if (group.total > group.results.length) {
                    <button hlmCommandItem [value]="'viewall:' + group.source" (selected)="onViewAll(group)">
                      <ng-icon name="lucideSearch" />
                      <span class="text-muted-foreground">View all ({{ group.total }}) in {{ group.label }}</span>
                    </button>
                  }
                </hlm-command-group>
              }
            }
          }
        </hlm-command-list>
      </hlm-command>
    </hlm-command-dialog>
  `,
})
export class CommandPaletteComponent {
  protected readonly globalSearch = inject(GlobalSearchService);
  private readonly searchApi = inject(SearchApiService);
  private readonly registry = inject(SearchPresenterRegistry);
  private readonly router = inject(Router);

  // Shared with the backend's MinQueryLength guard — kept in sync deliberately (cross-language).
  private static readonly MIN_QUERY_LENGTH = 2;
  // Per-source result cap requested from the API (mirrors the backend's default).
  private static readonly RESULTS_PER_SOURCE = 6;

  // Disable the brain command's client-side filter — the server is the source of truth.
  protected readonly alwaysVisible: CommandFilter = () => true;

  protected readonly searchInput = signal('');
  protected readonly loading = signal(false);
  protected readonly error = signal(false);

  protected readonly query = computed(() => this.searchInput().trim());

  // Ephemeral, in-memory query pipeline (no URL sync — the list page owns ?search).
  private readonly results = toSignal<SearchResponse | null>(
    toObservable(this.searchInput).pipe(
      // Clear a stale error as soon as the user edits, so the error state never lingers into a new query.
      tap(() => this.error.set(false)),
      debounceTime(300),
      // Dedupe and gate on the trimmed value so a trailing-space edit isn't a fresh request.
      map((q) => q.trim()),
      distinctUntilChanged(),
      switchMap((q) => {
        if (q.length < CommandPaletteComponent.MIN_QUERY_LENGTH) {
          this.loading.set(false);
          return of<SearchResponse | null>(null);
        }
        this.loading.set(true);
        return this.searchApi.search({ Q: q, Limit: CommandPaletteComponent.RESULTS_PER_SOURCE }).pipe(
          map((r) => {
            this.loading.set(false);
            return r;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  protected readonly groups = computed<SearchGroup[]>(() => this.results()?.groups ?? []);

  // Single source of truth for what the result list renders. Treating "no response yet"
  // (results === null) as loading is what prevents a flash of "No results found." during the
  // debounce window before the first request lands.
  protected readonly state = computed<'hint' | 'loading' | 'error' | 'empty' | 'results'>(() => {
    if (this.query().length < CommandPaletteComponent.MIN_QUERY_LENGTH) return 'hint';
    if (this.loading()) return 'loading';
    if (this.error()) return 'error';
    if (this.results() === null) return 'loading';
    return this.groups().length === 0 ? 'empty' : 'results';
  });

  constructor() {
    // Clear the query whenever the palette closes so a reopen starts blank (no stale results).
    effect(() => {
      if (!this.globalSearch.open()) {
        this.searchInput.set('');
      }
    });
  }

  @HostListener('document:keydown', ['$event'])
  protected onGlobalKeydown(event: KeyboardEvent): void {
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      this.globalSearch.toggle();
    }
  }

  protected onStateChange(state: BrnDialogState): void {
    // Esc / overlay click closes the brain dialog — keep the service in sync.
    if (state === 'closed') {
      this.globalSearch.close();
    }
  }

  protected iconFor(hit: SearchHit): string {
    return this.registry.get(hit.source)?.icon(hit) ?? 'lucideSearch';
  }

  protected onSelect(hit: SearchHit): void {
    const presenter = this.registry.get(hit.source);
    if (presenter) {
      this.router.navigate(presenter.navigate(hit));
    }
    this.globalSearch.close();
  }

  protected onViewAll(group: SearchGroup): void {
    const presenter = this.registry.get(group.source);
    if (presenter) {
      const { commands, queryParams } = presenter.viewAll(this.query());
      this.router.navigate(commands, { queryParams });
    }
    this.globalSearch.close();
  }
}
