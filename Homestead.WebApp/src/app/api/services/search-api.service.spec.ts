import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { SearchApiService } from './search-api.service';

describe('SearchApiService', () => {
  let service: SearchApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(SearchApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('search sends GET to /api/v1/search with query params and unwraps body', () => {
    const mockBody = {
      query: 'drill',
      groups: [{ source: 'inventory', label: 'Inventory', total: 1, results: [] }],
    };

    service.search({ Q: 'drill', Limit: 6 }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });

    const req = httpTesting.expectOne(r => r.url === '/api/v1/search');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('Q')).toBe('drill');
    expect(req.request.params.get('Limit')).toBe('6');
    req.flush(mockBody);
  });
});
