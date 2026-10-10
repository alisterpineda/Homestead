import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CommodityTableComponent } from './commodity-table.component';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

function commodity(overrides: Partial<CommodityResponse>): CommodityResponse {
  return {
    id: crypto.randomUUID(),
    canDelete: true,
    displayPrecision: 2,
    exchange: null,
    isSystemManaged: false,
    kind: 'Currency',
    latestPriceAt: null,
    name: 'Name',
    priceCount: 0,
    symbol: 'SYM',
    ...overrides,
  };
}

describe('CommodityTableComponent', () => {
  let fixture: ComponentFixture<CommodityTableComponent>;

  function renderedSymbols(): string[] {
    return Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('tbody tr')).map(
      tr => tr.querySelector('td')!.textContent!.trim(),
    );
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    fixture = TestBed.createComponent(CommodityTableComponent);
  });

  it('sorts rows by symbol ascending by default', () => {
    fixture.componentRef.setInput('commodities', [
      commodity({ symbol: 'USD' }),
      commodity({ symbol: 'CAD' }),
      commodity({ symbol: 'EUR' }),
    ]);
    fixture.detectChanges();

    expect(renderedSymbols()).toEqual(['CAD', 'EUR', 'USD']);
  });

  it('orders symbols with digits numerically, not character by character', () => {
    fixture.componentRef.setInput('commodities', [
      commodity({ symbol: 'A10' }),
      commodity({ symbol: 'B1' }),
      commodity({ symbol: 'A9' }),
    ]);
    fixture.detectChanges();

    expect(renderedSymbols()).toEqual(['A9', 'A10', 'B1']);
  });

  it('sorts by kind with symbol as the tie-break, following the primary direction', () => {
    fixture.componentRef.setInput('commodities', [
      commodity({ symbol: 'VFV', kind: 'Security' }),
      commodity({ symbol: 'USD', kind: 'Currency' }),
      commodity({ symbol: 'CAD', kind: 'Currency' }),
    ]);
    fixture.componentRef.setInput('sortBy', 'kind');
    fixture.componentRef.setInput('sortDir', 'asc');
    fixture.detectChanges();
    expect(renderedSymbols()).toEqual(['CAD', 'USD', 'VFV']);

    fixture.componentRef.setInput('sortDir', 'desc');
    fixture.detectChanges();
    expect(renderedSymbols()).toEqual(['VFV', 'USD', 'CAD']);
  });

  it('keeps rows without a latest price last in both sort directions', () => {
    fixture.componentRef.setInput('commodities', [
      commodity({ symbol: 'USD', kind: 'Currency' }),
      commodity({ symbol: 'QBBB', kind: 'Security', latestPriceAt: '2026-03-01T00:00:00Z' }),
      commodity({ symbol: 'QAAA', kind: 'Security', latestPriceAt: '2026-01-01T00:00:00Z' }),
    ]);
    fixture.componentRef.setInput('sortBy', 'latestPriceAt');
    fixture.componentRef.setInput('sortDir', 'asc');
    fixture.detectChanges();
    expect(renderedSymbols()).toEqual(['QAAA', 'QBBB', 'USD']);

    fixture.componentRef.setInput('sortDir', 'desc');
    fixture.detectChanges();
    expect(renderedSymbols()).toEqual(['QBBB', 'QAAA', 'USD']);
  });

  it('emits a descending sort change when the ascending-sorted header is clicked', () => {
    fixture.componentRef.setInput('commodities', [commodity({ symbol: 'USD' })]);
    const emitted: unknown[] = [];
    fixture.componentInstance.sortChange.subscribe(e => emitted.push(e));
    fixture.detectChanges();

    const symbolButton = Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('thead button')).find(
      b => b.textContent?.trim() === 'Symbol',
    );
    symbolButton!.click();

    expect(emitted).toEqual([{ sortBy: 'symbol', sortDir: 'desc' }]);
  });

  it('emits a sort change when a header is clicked', () => {
    fixture.componentRef.setInput('commodities', [commodity({ symbol: 'USD' })]);
    const emitted: unknown[] = [];
    fixture.componentInstance.sortChange.subscribe(e => emitted.push(e));
    fixture.detectChanges();

    const nameButton = Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('thead button')).find(
      b => b.textContent?.trim() === 'Name',
    );
    nameButton!.click();

    expect(emitted).toEqual([{ sortBy: 'name', sortDir: 'asc' }]);
  });
});
