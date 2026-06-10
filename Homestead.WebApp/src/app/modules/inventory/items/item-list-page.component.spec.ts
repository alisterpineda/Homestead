import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import { ItemListPageComponent } from './item-list-page.component';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { InventoryAreaApiService } from '@/api/services/inventory-area-api.service';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { AreaResponse } from '@/api/generated/models/area-response';
import type { ItemStatus } from '@/api/generated/models/item-status';

// brn-select (in child sheets) uses ResizeObserver; safe to stub for the rendered template.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

function makeItem(over: Partial<ItemResponse>): ItemResponse {
  return {
    id: 'x',
    name: 'Item',
    fungibility: 'NonFungible',
    status: 'Active',
    isContainer: false,
    path: [],
    createdAt: '',
    updatedAt: '',
    ...over,
  };
}

const GARAGE = 'garage';

// chest(garage) ← wrench, bin(garage), archivedInChest ; bin ← screw(grandchild) ; floating(unplaced)
const CHEST = makeItem({ id: 'chest', name: 'Tool chest', isContainer: true, areaId: GARAGE, resolvedAreaId: GARAGE });
const WRENCH = makeItem({ id: 'wrench', name: 'Wrench', parentItemId: 'chest', resolvedAreaId: GARAGE });
const BIN = makeItem({
  id: 'bin', name: 'Bin', isContainer: true, parentItemId: 'chest', resolvedAreaId: GARAGE,
  path: [
    { id: GARAGE, name: 'Garage', kind: 'Area' },
    { id: 'chest', name: 'Tool chest', kind: 'Container' },
  ],
});
const SCREW = makeItem({ id: 'screw', name: 'Screw', parentItemId: 'bin', resolvedAreaId: GARAGE });
const ARCHIVED = makeItem({ id: 'archived', name: 'Old rag', status: 'Archived' as ItemStatus, parentItemId: 'chest', resolvedAreaId: GARAGE });
const FLOATING = makeItem({ id: 'floating', name: 'Floating thing' });

class StubItemApi {
  // Honors the server-side ParentItemId filter the drill-down now uses.
  getItems = (p: { ParentItemId?: string }) => {
    const items = p?.ParentItemId ? ITEMS.filter(i => i.parentItemId === p.ParentItemId) : ITEMS;
    return of({ items, page: 1, pageSize: 200, totalCount: items.length });
  };
  getItem = (p: { id: string }) => {
    const found = ITEMS.find(i => i.id === p.id);
    return found ? of(found) : throwError(() => new Error('404'));
  };
  patchItem = () => of(makeItem({}));
  deleteItem = () => of(undefined);
}
class StubAreaApi {
  getAreas = () => of({ items: AREAS, page: 1, pageSize: 200, totalCount: AREAS.length });
}

const ITEMS: ItemResponse[] = [CHEST, WRENCH, BIN, SCREW, ARCHIVED, FLOATING];
const AREAS: AreaResponse[] = [{ id: GARAGE, name: 'Garage', liveItemCount: 0, createdAt: '', updatedAt: '' }];

describe('ItemListPageComponent', () => {
  let fixture: ComponentFixture<ItemListPageComponent>;
  let component: ItemListPageComponent;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: InventoryItemApiService, useValue: new StubItemApi() },
        { provide: InventoryAreaApiService, useValue: new StubAreaApi() },
      ],
    });
    fixture = TestBed.createComponent(ItemListPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  function ids(items: ItemResponse[]): string[] {
    return items.map(i => i.id).sort();
  }

  // Let the drill-down's server fetch (getItem + getItems(ParentItemId)) resolve.
  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('loads all items', () => {
    expect(component.allItems().length).toBe(ITEMS.length);
  });

  it('drill-down scopes to a container\'s direct children (grandchildren excluded)', async () => {
    component.onViewContents(CHEST);
    await settle();
    expect(ids(component['scopedItems']())).toEqual(['archived', 'bin', 'wrench']);
    // Show-inactive off by default hides the archived direct child.
    expect(ids(component.visibleItems())).toEqual(['bin', 'wrench']);
    // The grandchild (screw, under bin) is never in chest's direct-children scope.
    expect(component['scopedItems']().some(i => i.id === 'screw')).toBe(false);
  });

  it('resolved-area filter matches nested items', () => {
    component.areaFilter.set(GARAGE);
    // Every item that walks up to Garage — including the nested grandchild screw.
    expect(ids(component['scopedItems']())).toEqual(['archived', 'bin', 'chest', 'screw', 'wrench']);
    expect(component['scopedItems']().some(i => i.id === 'floating')).toBe(false);
  });

  it('resolved-unplaced bucket matches items with no resolved area', () => {
    component.areaFilter.set('unplaced');
    expect(ids(component['scopedItems']())).toEqual(['floating']);
  });

  it('counts inactive direct children inside a drilled container', async () => {
    component.containerFilter.set('chest');
    await settle();
    expect(component.inactiveCount()).toBe(1);
  });

  it('clears container scope when an area filter is picked or filters are reset', () => {
    component.containerFilter.set('chest');
    component.selectAreaFilter(GARAGE);
    expect(component.containerFilter()).toBeNull();
    expect(component.areaFilter()).toBe(GARAGE);

    component.containerFilter.set('chest');
    component.resetFilters();
    expect(component.containerFilter()).toBeNull();
    expect(component.areaFilter()).toBe('all');
  });

  it('builds a breadcrumb from the container path plus its own name', async () => {
    component.onViewContents(BIN);
    await settle();
    const crumb = component.breadcrumb();
    expect(crumb.map(s => s.name)).toEqual(['Garage', 'Tool chest', 'Bin']);
    expect(crumb[crumb.length - 1].id).toBe('bin');
  });

  it('keeps the breadcrumb exit reachable when the drilled container metadata is missing', async () => {
    // Container fetch fails / returns nothing → containerItem is null, but the scope (and its exit)
    // must remain: the breadcrumb block is gated on containerFilter, not on the loaded metadata.
    component.containerFilter.set('does-not-exist');
    await settle();
    expect(component.containerItem()).toBeNull();
    expect(component.containerFilter()).toBe('does-not-exist');
    component.clearContainerFilter();
    expect(component.containerFilter()).toBeNull();
  });
});
