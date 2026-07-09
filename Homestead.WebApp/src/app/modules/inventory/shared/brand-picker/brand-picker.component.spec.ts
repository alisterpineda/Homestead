import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { BrandPickerComponent, type BrandPickerValue } from './brand-picker.component';
import { InventoryBrandApiService } from '@/api/services/inventory-brand-api.service';
import type { BrandResponse } from '@/api/generated/models/brand-response';

describe('BrandPickerComponent', () => {
  let getBrands: ReturnType<typeof vi.fn>;
  let createBrand: ReturnType<typeof vi.fn>;

  function setup(): BrandPickerComponent {
    const fixture = TestBed.createComponent(BrandPickerComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  function setupFixture(): ComponentFixture<BrandPickerComponent> {
    const fixture = TestBed.createComponent(BrandPickerComponent);
    fixture.detectChanges();
    return fixture;
  }

  const brand = (id: string, name: string): BrandResponse => ({
    id, name, itemCount: 0, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z',
  });

  beforeEach(() => {
    getBrands = vi.fn().mockReturnValue(of({ items: [], totalCount: 0 }));
    createBrand = vi.fn().mockReturnValue(of({ id: 'brand-1', name: 'Apple' } as BrandResponse));
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        { provide: InventoryBrandApiService, useValue: { getBrands, createBrand } },
      ],
    });
  });

  it('creates', () => {
    expect(setup()).toBeTruthy();
  });

  it('offers the create row for a non-empty search with no matching results', () => {
    const c = setup();
    expect(c.showCreateRow()).toBe(false); // empty search
    c.searchTerm.set('Apple');
    expect(c.showCreateRow()).toBe(true);
  });

  it('create flow: calls createBrand and emits the created brand', () => {
    const c = setup();
    const emitted: (BrandPickerValue | null)[] = [];
    c.valueChange.subscribe(v => emitted.push(v));

    c.searchTerm.set('  Apple  ');
    c.onCreate();

    expect(createBrand).toHaveBeenCalledTimes(1);
    expect(createBrand.mock.calls[0][0]).toEqual({ body: { name: 'Apple' } });
    expect(emitted).toEqual([{ id: 'brand-1', name: 'Apple' }]);
    // Search resets and the picker isn't stuck in the creating state.
    expect(c.searchTerm()).toBe('');
    expect(c.creating()).toBe(false);
  });

  it('onSelect emits the picked brand and closes', () => {
    const c = setup();
    const emitted: (BrandPickerValue | null)[] = [];
    c.valueChange.subscribe(v => emitted.push(v));

    c.onSelect({ id: 'b2', name: 'Sony' });

    expect(emitted).toEqual([{ id: 'b2', name: 'Sony' }]);
    expect(c.popoverState()).toBe('closed');
  });

  it('onClear emits null (unattributed)', () => {
    const c = setup();
    const emitted: (BrandPickerValue | null)[] = [];
    c.valueChange.subscribe(v => emitted.push(v));

    c.onClear();

    expect(emitted).toEqual([null]);
  });

  it('does not create when the search is blank', () => {
    const c = setup();
    c.searchTerm.set('   ');
    c.onCreate();
    expect(createBrand).not.toHaveBeenCalled();
  });

  // ----- Merge-dialog opt-ins -----

  it('excludeId filters the source brand out of the results', async () => {
    vi.useFakeTimers();
    try {
      getBrands.mockReturnValue(of({ items: [brand('src', 'Apple Inc'), brand('tgt', 'Apple')], totalCount: 2 }));
      const fixture = setupFixture();
      const c = fixture.componentInstance;
      fixture.componentRef.setInput('excludeId', 'src');
      c.searchTerm.set('App');
      // Drain the 200ms search debounce so the results pipeline resolves.
      await vi.advanceTimersByTimeAsync(250);

      expect(c.results().map(b => b.id)).toEqual(['tgt']);
    } finally {
      vi.useRealTimers();
    }
  });

  it('suppresses the create row when allowCreate is false', () => {
    const fixture = setupFixture();
    const c = fixture.componentInstance;
    fixture.componentRef.setInput('allowCreate', false);
    c.searchTerm.set('Brand New');
    fixture.detectChanges();

    expect(c.showCreateRow()).toBe(false);
  });

  it('seeds the search box from initialSearch', () => {
    const fixture = setupFixture();
    fixture.componentRef.setInput('initialSearch', 'Apple');
    fixture.detectChanges();

    expect(fixture.componentInstance.searchTerm()).toBe('Apple');
  });
});
