import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { PartyApiService } from './party-api.service';

describe('PartyApiService', () => {
  let service: PartyApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(PartyApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('getParties sends GET and unwraps body', () => {
    const mockBody = { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 };
    service.getParties({ Page: 1, PageSize: 10 }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/documents/parties');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('createParty sends POST and unwraps body', () => {
    const mockBody = { id: '1', name: 'Test' };
    service.createParty({ body: { name: 'Test' } }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/documents/parties');
    expect(req.request.method).toBe('POST');
    req.flush(mockBody);
  });

  it('updateParty sends PUT and unwraps body', () => {
    const mockBody = { id: '1', name: 'Updated' };
    service.updateParty({ id: '1', body: { name: 'Updated' } }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/documents/parties/1');
    expect(req.request.method).toBe('PUT');
    req.flush(mockBody);
  });

  it('bulkDeleteParties sends POST and unwraps body', () => {
    const mockBody = { deletedCount: 2 };
    service.bulkDeleteParties({ body: { ids: ['1', '2'] } }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/documents/parties/bulk-delete');
    expect(req.request.method).toBe('POST');
    req.flush(mockBody);
  });
});
