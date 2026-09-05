import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { LocationPickerComponent, type ItemLocationValue } from './location-picker.component';
import type { AreaResponse } from '@/api/generated/models/area-response';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ItemStatus } from '@/api/generated/models/item-status';

function area(id: string, name: string): AreaResponse {
  return { id, name, liveItemCount: 0, createdAt: '', updatedAt: '' };
}

function container(id: string, name: string, opts: { status?: ItemStatus; parentItemId?: string | null } = {}): DurableResponse {
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

// An assembly is a non-container that has components.
function assembly(id: string, name: string, opts: { status?: ItemStatus; parentItemId?: string | null } = {}): DurableResponse {
  return { ...container(id, name, opts), isContainer: false, hasComponents: true };
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

  async function openAndLoad(areas: AreaResponse[], containers: DurableResponse[]): Promise<void> {
    component['popoverState'].set('open');
    fixture.detectChanges();
    await fixture.whenStable();
    httpMock.expectOne(r => r.url.includes('/inventory/areas'))
      .flush({ items: areas, page: 1, pageSize: 200, totalCount: areas.length });
    httpMock.expectOne(r => r.url.includes('/inventory/durables'))
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
      container('f', 'F', { status: 'Archived' }),
    ];
    await openAndLoad([], containers);

    const ids = component['filteredContainers']().map(c => c.id);
    // 'a' is the item, 'b'/'c'/'d' are its subtree (the walk crosses terminal 'c'), 'f' is terminal.
    expect(ids).toEqual(['e']);
  });

  it('splits holders into Containers (isContainer) and Part-of (non-container with components) groups', async () => {
    // A regular leaf item (neither a container nor holding components) appears in neither group.
    const leaf: DurableResponse = {
      id: 'drill', name: 'Drill', fungibility: 'NonFungible', status: 'Active',
      isContainer: false, path: [], createdAt: '', updatedAt: '',
    };
    await openAndLoad([], [container('chest', 'Tool chest'), assembly('pc', 'Gaming PC'), leaf]);

    expect(component['filteredContainers']().map(c => c.id)).toEqual(['chest']);
    expect(component['filteredAssemblies']().map(c => c.id)).toEqual(['pc']);
  });

  it('fetches live durables without a DurableKinds filter (the picker filters client-side)', async () => {
    component['popoverState'].set('open');
    fixture.detectChanges();
    await fixture.whenStable();
    httpMock.expectOne(r => r.url.includes('/inventory/areas')).flush({ items: [], page: 1, pageSize: 200, totalCount: 0 });
    const req = httpMock.expectOne(r => r.url.includes('/inventory/durables'));
    expect(req.request.params.has('DurableKinds')).toBe(false);
    expect(req.request.params.get('IncludeInactive')).toBe('true');
    req.flush({ items: [], page: 1, pageSize: 200, totalCount: 0 });
  });

  it('selecting an assembly emits a container-kind value (mapped to parentItemId by consumers)', async () => {
    await openAndLoad([], [assembly('pc', 'Gaming PC')]);

    const emitted: ItemLocationValue[] = [];
    component.valueChange.subscribe(v => emitted.push(v));
    component['onSelectContainer'](assembly('pc', 'Gaming PC'));

    expect(emitted).toEqual([{ kind: 'container', id: 'pc' }]);
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
