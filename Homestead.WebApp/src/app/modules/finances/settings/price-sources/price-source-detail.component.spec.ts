import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { PriceSourceDetailComponent } from './price-source-detail.component';
import { FinancesPriceSourcesApiService } from '@/api/services/finances-price-sources-api.service';
import type { GetPriceSourceResponse } from '@/api/generated/models/get-price-source-response';

function makeResponse(overrides: Partial<GetPriceSourceResponse> = {}): GetPriceSourceResponse {
  return {
    source: 'BankOfCanada',
    displayName: 'Bank of Canada',
    kind: 'External',
    isEnabled: false,
    configSchema: { fields: [] },
    supportedPairs: [],
    coveredSecurityExchanges: [],
    coveredQuoteCurrencies: [],
    priceCount: 0,
    ...overrides,
  };
}

class FakeApi {
  response: GetPriceSourceResponse = makeResponse();
  get() { return of<GetPriceSourceResponse>(this.response); }
  patch() { return of({ source: 'BankOfCanada', isEnabled: true }); }
  upsertSchedule() { return of({ id: 'abc', nextRunAt: '2026-04-29T00:00:00Z' }); }
  deleteSchedule() { return of(undefined); }
  runNow() { return of({ jobId: 'xyz' }); }
}

describe('PriceSourceDetailComponent', () => {
  let api: FakeApi;

  beforeEach(() => {
    api = new FakeApi();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: FinancesPriceSourcesApiService, useValue: api },
      ],
    });
  });

  it('loads detail for the source', () => {
    const fixture = TestBed.createComponent(PriceSourceDetailComponent);
    fixture.detectChanges();
    expect(fixture.componentInstance.data()).not.toBeNull();
    expect(fixture.componentInstance.data()!.source).toBe('BankOfCanada');
  });

  it('initialises isEnabled from response', () => {
    const fixture = TestBed.createComponent(PriceSourceDetailComponent);
    fixture.detectChanges();
    expect(fixture.componentInstance.isEnabled()).toBe(false);
  });

  it('renders a badge per supported pair', () => {
    api.response = makeResponse({
      supportedPairs: [
        { commoditySymbol: 'EUR', quoteCurrencySymbol: 'CAD', exchange: null },
        { commoditySymbol: 'USD', quoteCurrencySymbol: 'CAD', exchange: null },
      ],
    });
    const fixture = TestBed.createComponent(PriceSourceDetailComponent);
    fixture.detectChanges();
    const section = fixture.nativeElement.querySelector('[data-testid="coverage-section"]') as HTMLElement | null;
    expect(section).not.toBeNull();
    expect(section!.textContent).toContain('Supported pairs');
    expect(section!.textContent).toContain('EUR → CAD');
    expect(section!.textContent).toContain('USD → CAD');
  });

  it('renders covered exchanges with quote currencies from the response', () => {
    api.response = makeResponse({
      source: 'AlphaVantage',
      displayName: 'Alpha Vantage',
      coveredSecurityExchanges: ['NASDAQ', 'NYSE', 'AMEX', 'TSX'],
      coveredQuoteCurrencies: ['USD', 'CAD'],
    });
    const fixture = TestBed.createComponent(PriceSourceDetailComponent);
    fixture.detectChanges();
    const section = fixture.nativeElement.querySelector('[data-testid="coverage-section"]') as HTMLElement | null;
    expect(section).not.toBeNull();
    expect(section!.textContent).toContain('Covered exchanges');
    expect(section!.textContent).toContain('NASDAQ');
    expect(section!.textContent).toContain('TSX');
    expect(section!.textContent).toContain('USD');
    expect(section!.textContent).toContain('CAD');
  });

  it('omits the coverage section when both lists are empty', () => {
    api.response = makeResponse();
    const fixture = TestBed.createComponent(PriceSourceDetailComponent);
    fixture.detectChanges();
    const section = fixture.nativeElement.querySelector('[data-testid="coverage-section"]');
    expect(section).toBeNull();
  });
});
