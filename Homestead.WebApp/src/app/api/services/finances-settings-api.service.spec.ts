import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { FinancesSettingsApiService } from './finances-settings-api.service';

describe('FinancesSettingsApiService', () => {
  let service: FinancesSettingsApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(FinancesSettingsApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('getFinanceSettings sends GET', () => {
    const mockBody = { displayCurrencyId: null };
    service.getFinanceSettings().subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/finances/settings');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('patchFinanceSettings sends PATCH', () => {
    const mockBody = { displayCurrencyId: 'cad' };
    service.patchFinanceSettings({
      body: [{ op: 'replace', path: '/displayCurrencyId', value: 'cad' }],
    }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/finances/settings');
    expect(req.request.method).toBe('PATCH');
    req.flush(mockBody);
  });
});
