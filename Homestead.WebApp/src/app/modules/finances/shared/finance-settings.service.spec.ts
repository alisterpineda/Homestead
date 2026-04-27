import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { FinanceSettingsService } from './finance-settings.service';

describe('FinanceSettingsService', () => {
  let service: FinanceSettingsService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(FinanceSettingsService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('load() fetches settings and resolves the currency commodity', () => {
    service.load();
    const settingsReq = httpTesting.expectOne('/api/v1/finances/settings');
    settingsReq.flush({ displayCurrencyId: 'cad' });

    const commodityReq = httpTesting.expectOne('/api/v1/finances/commodities/cad');
    commodityReq.flush({ id: 'cad', symbol: 'CAD', name: 'Canadian Dollar', kind: 'Currency', displayPrecision: 2, exchange: null, isSystemManaged: true });

    expect(service.loaded()).toBe(true);
    expect(service.displayCurrencyId()).toBe('cad');
    expect(service.displayCurrency()?.symbol).toBe('CAD');
  });

  it('load() handles a null display currency', () => {
    service.load();
    const settingsReq = httpTesting.expectOne('/api/v1/finances/settings');
    settingsReq.flush({ displayCurrencyId: null });

    httpTesting.expectNone('/api/v1/finances/commodities/cad');
    expect(service.loaded()).toBe(true);
    expect(service.displayCurrencyId()).toBeNull();
    expect(service.hasDisplayCurrency()).toBe(false);
  });

  it('load() is idempotent — second call does not re-fetch', () => {
    service.load();
    httpTesting.expectOne('/api/v1/finances/settings').flush({ displayCurrencyId: null });

    service.load();
    httpTesting.expectNone('/api/v1/finances/settings');
  });

  it('setDisplayCurrencyId PATCHes settings and refreshes signals', () => {
    service.setDisplayCurrencyId('usd').subscribe();

    const patchReq = httpTesting.expectOne('/api/v1/finances/settings');
    expect(patchReq.request.method).toBe('PATCH');
    patchReq.flush({ displayCurrencyId: 'usd' });

    const commodityReq = httpTesting.expectOne('/api/v1/finances/commodities/usd');
    commodityReq.flush({ id: 'usd', symbol: 'USD', name: 'US Dollar', kind: 'Currency', displayPrecision: 2, exchange: null, isSystemManaged: true });

    expect(service.displayCurrencyId()).toBe('usd');
    expect(service.displayCurrency()?.symbol).toBe('USD');
  });
});
