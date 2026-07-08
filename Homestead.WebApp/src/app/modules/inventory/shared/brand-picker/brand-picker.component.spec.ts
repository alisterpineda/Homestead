import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { BrandPickerComponent } from './brand-picker.component';
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
    const emitted: (BrandResponse | null)[] = [];
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
    const emitted: (BrandResponse | null)[] = [];
    c.valueChange.subscribe(v => emitted.push(v));

    c.onSelect({ id: 'b2', name: 'Sony' });

    expect(emitted).toEqual([{ id: 'b2', name: 'Sony' }]);
    expect(c.popoverState()).toBe('closed');
  });

  it('onClear emits null (unattributed)', () => {
    const c = setup();
    const emitted: (BrandResponse | null)[] = [];
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
});
