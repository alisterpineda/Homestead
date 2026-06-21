import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { CommandPaletteComponent } from './command-palette.component';
import { GlobalSearchService } from '@/shared/services/global-search.service';
import { SearchApiService } from '@/api/services';
import type { SearchResponse } from '@/api/generated/models/search-response';
import type { SearchHit } from '@/api/generated/models/search-hit';
import type { SearchGroup } from '@/api/generated/models/search-group';

describe('CommandPaletteComponent', () => {
  let fixture: ComponentFixture<CommandPaletteComponent>;
  let component: CommandPaletteComponent;
  let globalSearch: GlobalSearchService;
  let router: Router;
  const searchMock = vi.fn();

  // Reach the component's protected members for assertions.
  const internals = () =>
    component as unknown as {
      searchInput: { set: (v: string) => void };
      groups: () => SearchGroup[];
      state: () => 'hint' | 'loading' | 'error' | 'empty' | 'results';
      onSelect: (hit: SearchHit) => void;
      onViewAll: (group: SearchGroup) => void;
      onStateChange: (state: 'open' | 'closed') => void;
    };

  const response = (groups: SearchGroup[]): SearchResponse => ({ query: 'drill', groups });
  const hit = (over: Partial<SearchHit> = {}): SearchHit => ({
    source: 'inventory',
    type: 'durable',
    id: 'abc',
    title: 'Drill',
    subtitle: null,
    badge: null,
    ...over,
  });

  beforeEach(() => {
    vi.useFakeTimers();
    searchMock.mockReturnValue(of(response([])));

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SearchApiService, useValue: { search: searchMock } },
      ],
    });

    fixture = TestBed.createComponent(CommandPaletteComponent);
    component = fixture.componentInstance;
    globalSearch = TestBed.inject(GlobalSearchService);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture.detectChanges();
  });

  afterEach(() => {
    searchMock.mockReset();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('debounces and calls the API with the query when >= 2 chars', () => {
    internals().searchInput.set('drill');
    vi.advanceTimersByTime(300);

    expect(searchMock).toHaveBeenCalledWith({ Q: 'drill', Limit: 6 });
  });

  it('does not call the API for queries shorter than 2 chars', () => {
    internals().searchInput.set('a');
    vi.advanceTimersByTime(300);

    expect(searchMock).not.toHaveBeenCalled();
  });

  it('exposes the server result groups via the groups signal', () => {
    const groups: SearchGroup[] = [
      { source: 'inventory', label: 'Inventory', total: 1, results: [hit()] },
    ];
    searchMock.mockReturnValue(of(response(groups)));

    internals().searchInput.set('drill');
    vi.advanceTimersByTime(300);
    fixture.detectChanges();

    expect(internals().groups()).toEqual(groups);
  });

  it('shows loading (not a premature empty state) while a query is pending before results land', () => {
    // Query is >= 2 chars but the debounce has not fired, so no response exists yet. This must read
    // as "loading", never "empty" — the flash-of-no-results regression guard.
    internals().searchInput.set('drill');

    expect(internals().state()).toBe('loading');

    vi.advanceTimersByTime(300);
    expect(searchMock).toHaveBeenCalled();
  });

  it('surfaces an error state (not "no results") when the search request fails', () => {
    searchMock.mockReturnValue(throwError(() => new Error('boom')));

    internals().searchInput.set('drill');
    vi.advanceTimersByTime(300);

    expect(internals().state()).toBe('error');
    expect(internals().groups()).toEqual([]);
  });

  it('does not re-request when only trailing whitespace changes', () => {
    internals().searchInput.set('drill');
    vi.advanceTimersByTime(300);
    expect(searchMock).toHaveBeenCalledTimes(1);

    internals().searchInput.set('drill ');
    vi.advanceTimersByTime(300);
    expect(searchMock).toHaveBeenCalledTimes(1);
  });

  it('navigates via the presenter and closes the palette on select', () => {
    globalSearch.openPalette();

    internals().onSelect(hit({ id: 'item-1' }));

    expect(router.navigate).toHaveBeenCalledWith(['/inventory/items', 'item-1']);
    expect(globalSearch.open()).toBe(false);
  });

  it('routes consumables through the same item detail route', () => {
    internals().onSelect(hit({ type: 'consumable', id: 'c-9' }));
    expect(router.navigate).toHaveBeenCalledWith(['/inventory/items', 'c-9']);
  });

  it('navigates to the list deep link with the search query on view-all', () => {
    internals().searchInput.set('drill');

    const group: SearchGroup = { source: 'inventory', label: 'Inventory', total: 8, results: [] };
    internals().onViewAll(group);

    expect(router.navigate).toHaveBeenCalledWith(['/inventory/items'], {
      queryParams: { search: 'drill' },
    });
    expect(globalSearch.open()).toBe(false);
  });

  it('opens the palette on Ctrl/Cmd+K', () => {
    expect(globalSearch.open()).toBe(false);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', metaKey: true }));

    expect(globalSearch.open()).toBe(true);
  });

  it('syncs service state when the dialog closes (Esc / overlay)', () => {
    globalSearch.openPalette();

    internals().onStateChange('closed');

    expect(globalSearch.open()).toBe(false);
  });
});
