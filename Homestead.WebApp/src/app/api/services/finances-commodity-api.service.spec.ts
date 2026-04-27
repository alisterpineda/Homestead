import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { FinancesCommodityApiService } from './finances-commodity-api.service';

describe('FinancesCommodityApiService', () => {
  let service: FinancesCommodityApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(FinancesCommodityApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('getCommodities sends GET and unwraps body', () => {
    const mockBody = { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 };
    service.getCommodities({ Page: 1, PageSize: 50, Kind: 'Currency' }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/finances/commodities');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('getCommodity sends GET with path param', () => {
    const mockBody = { id: 'cad', symbol: 'CAD' };
    service.getCommodity({ id: 'cad' }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/finances/commodities/cad');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });
});
