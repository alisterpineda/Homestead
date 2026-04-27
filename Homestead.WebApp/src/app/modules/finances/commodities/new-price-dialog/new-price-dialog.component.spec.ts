import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideNativeDateAdapter } from '@spartan-ng/brain/date-time';
import { NewPriceDialogComponent } from './new-price-dialog.component';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

const veqt: CommodityResponse = {
  id: 'veqt',
  symbol: 'VEQT',
  exchange: 'TSX',
  name: 'Vanguard All-Equity ETF',
  kind: 'Security',
  displayPrecision: 4,
  isSystemManaged: false,
  canDelete: false,
  latestPriceAt: null,
  priceCount: 0,
};

describe('NewPriceDialogComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideNativeDateAdapter(),
      ],
    });
  });

  it('defaults the date to today and the time to a non-empty HH:MM', () => {
    const fixture = TestBed.createComponent(NewPriceDialogComponent);
    fixture.componentRef.setInput('commodity', veqt);
    const c = fixture.componentInstance;

    const today = new Date();
    today.setHours(0, 0, 0, 0);
    expect(c.dateValue()?.getTime()).toBe(today.getTime());
    expect(c.timeInput()).toMatch(/^\d{2}:\d{2}$/);
  });

  it('blocks submission until quote, rate, and time are all valid', () => {
    const fixture = TestBed.createComponent(NewPriceDialogComponent);
    fixture.componentRef.setInput('commodity', veqt);
    const c = fixture.componentInstance;

    expect(c.canSubmit()).toBe(false);

    c.quoteId.set('cad');
    c.rateInput.set('1.35');
    expect(c.canSubmit()).toBe(true);
  });

  it('blocks submission when the time is cleared (time is required)', () => {
    const fixture = TestBed.createComponent(NewPriceDialogComponent);
    fixture.componentRef.setInput('commodity', veqt);
    const c = fixture.componentInstance;

    c.quoteId.set('cad');
    c.rateInput.set('1.35');
    expect(c.canSubmit()).toBe(true);

    c.timeInput.set('');
    expect(c.canSubmit()).toBe(false);
  });

  it('blocks submission when the quote currency is the same as the commodity', () => {
    const fixture = TestBed.createComponent(NewPriceDialogComponent);
    fixture.componentRef.setInput('commodity', veqt);
    const c = fixture.componentInstance;

    c.quoteId.set(veqt.id);
    c.rateInput.set('1');
    expect(c.canSubmit()).toBe(false);
  });

  it('blocks submission when the rate is zero or negative', () => {
    const fixture = TestBed.createComponent(NewPriceDialogComponent);
    fixture.componentRef.setInput('commodity', veqt);
    const c = fixture.componentInstance;

    c.quoteId.set('cad');
    c.rateInput.set('0');
    expect(c.canSubmit()).toBe(false);

    c.rateInput.set('-1');
    expect(c.canSubmit()).toBe(false);
  });

  it('resets the form to defaults when the dialog opens', () => {
    const fixture = TestBed.createComponent(NewPriceDialogComponent);
    fixture.componentRef.setInput('commodity', veqt);
    const c = fixture.componentInstance;

    c.quoteId.set('cad');
    c.rateInput.set('1.35');
    c.timeInput.set('09:00');
    c.dateValue.set(new Date(2024, 0, 1));

    c.onStateChanged('open');

    expect(c.quoteId()).toBeNull();
    expect(c.rateInput()).toBe('');
    expect(c.timeInput()).toMatch(/^\d{2}:\d{2}$/);
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    expect(c.dateValue()?.getTime()).toBe(today.getTime());
  });
});
