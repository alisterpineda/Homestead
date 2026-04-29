import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideNativeDateAdapter } from '@spartan-ng/brain/date-time';
import { EditPriceDialogComponent } from './edit-price-dialog.component';
import type { CommodityPriceResponse } from '@/api/generated/models/commodity-price-response';

function makePrice(overrides: Partial<CommodityPriceResponse> = {}): CommodityPriceResponse {
  return {
    id: 'price-1',
    commodityId: 'veqt',
    quoteCurrencyId: 'cad',
    priceAt: '2026-04-27T14:30:00.000Z',
    rate: 35.42,
    source: 'Manual',
    createdByTransactionId: null,
    ...overrides,
  };
}

describe('EditPriceDialogComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideNativeDateAdapter(),
      ],
    });
  });

  it('populates date, time, and rate from the price when opened', () => {
    const fixture = TestBed.createComponent(EditPriceDialogComponent);
    const price = makePrice();
    fixture.componentRef.setInput('price', price);
    fixture.componentRef.setInput('state', 'open');
    TestBed.tick();

    const c = fixture.componentInstance;
    const original = new Date(price.priceAt);

    expect(c.dateValue()?.getFullYear()).toBe(original.getFullYear());
    expect(c.dateValue()?.getMonth()).toBe(original.getMonth());
    expect(c.dateValue()?.getDate()).toBe(original.getDate());
    expect(c.timeInput()).toBe(
      `${String(original.getHours()).padStart(2, '0')}:${String(original.getMinutes()).padStart(2, '0')}`,
    );
    expect(c.rateInput()).toBe('35.42');
  });

  it('does not enable submit when nothing has changed (local↔UTC roundtrip is stable)', () => {
    const fixture = TestBed.createComponent(EditPriceDialogComponent);
    fixture.componentRef.setInput('price', makePrice());
    fixture.componentRef.setInput('state', 'open');
    TestBed.tick();

    expect(fixture.componentInstance.canSubmit()).toBe(false);
  });

  it('enables submit when the rate changes', () => {
    const fixture = TestBed.createComponent(EditPriceDialogComponent);
    fixture.componentRef.setInput('price', makePrice());
    fixture.componentRef.setInput('state', 'open');
    TestBed.tick();

    const c = fixture.componentInstance;
    c.rateInput.set('40');
    expect(c.canSubmit()).toBe(true);
  });

  it('enables submit when the time changes', () => {
    const fixture = TestBed.createComponent(EditPriceDialogComponent);
    fixture.componentRef.setInput('price', makePrice());
    fixture.componentRef.setInput('state', 'open');
    TestBed.tick();

    const c = fixture.componentInstance;
    const [hh, mm] = c.timeInput().split(':').map(Number);
    const nextHh = String((hh + 1) % 24).padStart(2, '0');
    c.timeInput.set(`${nextHh}:${String(mm).padStart(2, '0')}`);
    expect(c.canSubmit()).toBe(true);
  });

  it('blocks submission when the rate is zero or negative', () => {
    const fixture = TestBed.createComponent(EditPriceDialogComponent);
    fixture.componentRef.setInput('price', makePrice());
    fixture.componentRef.setInput('state', 'open');
    TestBed.tick();

    const c = fixture.componentInstance;
    c.rateInput.set('0');
    expect(c.canSubmit()).toBe(false);

    c.rateInput.set('-1');
    expect(c.canSubmit()).toBe(false);
  });

  it('blocks submission when the time is cleared', () => {
    const fixture = TestBed.createComponent(EditPriceDialogComponent);
    fixture.componentRef.setInput('price', makePrice());
    fixture.componentRef.setInput('state', 'open');
    TestBed.tick();

    const c = fixture.componentInstance;
    c.rateInput.set('40');
    expect(c.canSubmit()).toBe(true);

    c.timeInput.set('');
    expect(c.canSubmit()).toBe(false);
  });
});
