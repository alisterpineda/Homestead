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
    isConfigured: false,
    configurationError: null,
    hasApiKey: null,
    baseUrl: 'https://www.bankofcanada.ca/valet/',
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
  patch() { return of({ source: 'BankOfCanada', pairAllowList: [] }); }
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

  function readout(fixture: { nativeElement: HTMLElement }): HTMLElement {
    return fixture.nativeElement.querySelector('[data-testid="provider-readout"]') as HTMLElement;
  }

  function runNowButton(fixture: { nativeElement: HTMLElement }): HTMLButtonElement {
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    return buttons.find(b => b.textContent?.includes('Run now'))!;
  }

  it('loads detail for the source', () => {
    const fixture = TestBed.createComponent(PriceSourceDetailComponent);
    fixture.detectChanges();
    expect(fixture.componentInstance.data()).not.toBeNull();
    expect(fixture.componentInstance.data()!.source).toBe('BankOfCanada');
  });

  // Enablement and credentials are deployment config, so the page only reports them.
  it('renders Enabled and Configured badges from the response', () => {
    api.response = makeResponse({ isEnabled: true, isConfigured: true });
    const fixture = TestBed.createComponent(PriceSourceDetailComponent);
    fixture.detectChanges();
    const text = readout(fixture).textContent!;
    expect(text).toContain('Enabled');
    expect(text).toContain('Configured');
    expect(text).not.toContain('Not configured');
    expect(text).toContain('PriceSources:BankOfCanada:Enabled');
    // Bank of Canada takes no key, so no API key row.
    expect(text).not.toContain('API key');
    expect(text).toContain('https://www.bankofcanada.ca/valet/');
  });

  // A disabled source never earns "Configured", so that badge would only restate "Disabled"; the
  // validation reason and key state still show so the operator knows what else to set.
  it('renders Disabled with the reason and API key state, without a Configured badge', () => {
    api.response = makeResponse({
      source: 'AlphaVantage',
      displayName: 'Alpha Vantage',
      isEnabled: false,
      isConfigured: false,
      configurationError: 'ApiKey is required.',
      hasApiKey: false,
    });
    const fixture = TestBed.createComponent(PriceSourceDetailComponent);
    fixture.detectChanges();
    const text = readout(fixture).textContent!;
    expect(text).toContain('Disabled');
    expect(text).not.toContain('Not configured');
    expect(text).toContain('ApiKey is required.');
    expect(text).toContain('API key');
    expect(text).toContain('Not set');
  });

  it('renders Enabled / Not configured with the reason when enabled but invalid', () => {
    api.response = makeResponse({
      source: 'AlphaVantage',
      displayName: 'Alpha Vantage',
      isEnabled: true,
      isConfigured: false,
      configurationError: 'ApiKey is required.',
      hasApiKey: false,
    });
    const fixture = TestBed.createComponent(PriceSourceDetailComponent);
    fixture.detectChanges();
    const text = readout(fixture).textContent!;
    expect(text).toContain('Enabled');
    expect(text).toContain('Not configured');
    expect(text).toContain('ApiKey is required.');
  });

  it('disables Run now when the source is disabled', () => {
    api.response = makeResponse({ isEnabled: false, isConfigured: false });
    const fixture = TestBed.createComponent(PriceSourceDetailComponent);
    fixture.detectChanges();
    expect(runNowButton(fixture).disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('This source is disabled');
  });

  it('disables Run now with the reason when enabled but not configured', () => {
    api.response = makeResponse({ isEnabled: true, isConfigured: false, configurationError: 'ApiKey is required.' });
    const fixture = TestBed.createComponent(PriceSourceDetailComponent);
    fixture.detectChanges();
    expect(runNowButton(fixture).disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('This source is not configured: ApiKey is required.');
  });

  it('enables Run now when enabled and configured', () => {
    api.response = makeResponse({ isEnabled: true, isConfigured: true });
    const fixture = TestBed.createComponent(PriceSourceDetailComponent);
    fixture.detectChanges();
    expect(runNowButton(fixture).disabled).toBe(false);
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
