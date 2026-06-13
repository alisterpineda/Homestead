import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { LocationPickerComponent, type ItemLocationValue } from './location-picker.component';
import type { AreaResponse } from '@/api/generated/models/area-response';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { ItemStatus } from '@/api/generated/models/item-status';

function area(id: string, name: string): AreaResponse {
  return { id, name, liveItemCount: 0, createdAt: '', updatedAt: '' };
}

function container(id: string, name: string, opts: { status?: ItemStatus; parentItemId?: string | null } = {}): ItemResponse {
  return {
    id,
    name,
    fungibility: 'NonFungible',
    status: opts.status ?? 'Active',
    isContainer: true,
    parentItemId: opts.parentItemId ?? null,
    path: [],
    createdAt: '',
    updatedAt: '',
  };
}

describe('LocationPickerComponent', () => {
  let fixture: ComponentFixture<LocationPickerComponent>;
  let component: LocationPickerComponent;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(LocationPickerComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  async function openAndLoad(areas: AreaResponse[], containers: ItemResponse[]): Promise<void> {
    component['popoverState'].set('open');
    fixture.detectChanges();
    await fixture.whenStable();
    httpMock.expectOne(r => r.url.includes('/inventory/areas'))
      .flush({ items: areas, page: 1, pageSize: 200, totalCount: areas.length });
    httpMock.expectOne(r => r.url.includes('/inventory/items'))
      .flush({ items: containers, page: 1, pageSize: 200, totalCount: containers.length });
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('emits discriminated values for unplaced / area / container', async () => {
    await openAndLoad([area('garage', 'Garage')], [container('chest', 'Tool chest')]);

    const emitted: ItemLocationValue[] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    component['onSelectUnplaced']();
    component['onSelectArea'](area('garage', 'Garage'));
    component['onSelectContainer'](container('chest', 'Tool chest'));

    expect(emitted).toEqual([
      { kind: 'unplaced' },
      { kind: 'area', id: 'garage' },
      { kind: 'container', id: 'chest' },
    ]);
  });

  it('displays live containers only but excludes the subtree across a terminal container', async () => {
    fixture.componentRef.setInput('excludeItemId', 'a');
    // a → b → c(Archived) → d, plus an unrelated live container e and a terminal-only f.
    const containers = [
      container('a', 'A'),
      container('b', 'B', { parentItemId: 'a' }),
      container('c', 'C', { status: 'Archived', parentItemId: 'b' }),
      container('d', 'D', { parentItemId: 'c' }),
      container('e', 'E'),
      container('f', 'F', { status: 'Sold' }),
    ];
    await openAndLoad([], containers);

    const ids = component['filteredContainers']().map(c => c.id);
    // 'a' is the item, 'b'/'c'/'d' are its subtree (the walk crosses terminal 'c'), 'f' is terminal.
    expect(ids).toEqual(['e']);
  });

  it('filters both areas and containers by the search term', async () => {
    await openAndLoad(
      [area('garage', 'Garage'), area('office', 'Office')],
      [container('garageShelf', 'Garage shelf'), container('toolbox', 'Toolbox')],
    );

    component['searchTerm'].set('gar');
    fixture.detectChanges();

    expect(component['filteredAreas']().map(n => n.area.id)).toEqual(['garage']);
    expect(component['filteredContainers']().map(c => c.id)).toEqual(['garageShelf']);
  });
});
