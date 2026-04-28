import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { FinancesReportsApiService } from './finances-reports-api.service';

describe('FinancesReportsApiService', () => {
  let service: FinancesReportsApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(FinancesReportsApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('getNetWorthHistory sends GET with range param and unwraps body', () => {
    const mockBody = {
      displayCurrencyId: 'cad-id',
      displayCurrencySymbol: 'CAD',
      granularity: 'Daily',
      range: 'OneMonth',
      from: '2026-04-01T23:59:59Z',
      to: '2026-04-28T23:59:59Z',
      points: [{ asOf: '2026-04-28T23:59:59Z', value: 1234.56 }],
      hasUnpriced: false,
      reason: null,
    };
    service.getNetWorthHistory({ Range: 'OneMonth' }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(
      r => r.url === '/api/v1/finances/reports/net-worth-history' && r.params.get('Range') === 'OneMonth',
    );
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });
});
