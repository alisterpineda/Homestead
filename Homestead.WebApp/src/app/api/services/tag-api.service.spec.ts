import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { TagApiService } from './tag-api.service';

describe('TagApiService', () => {
  let service: TagApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(TagApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('getTags sends GET to /api/v1/documents/tags and unwraps body', () => {
    const mockBody = { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 };
    service.getTags({ Page: 1, PageSize: 10 }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/documents/tags');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('createTag sends POST to /api/v1/documents/tags and unwraps body', () => {
    const mockBody = { id: '1', path: 'test' };
    service.createTag({ body: { path: 'test' } }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/documents/tags');
    expect(req.request.method).toBe('POST');
    req.flush(mockBody);
  });

  it('patchTag sends PATCH to /api/v1/documents/tags/{id} and unwraps body', () => {
    const mockBody = { id: '1', path: 'updated' };
    service.patchTag({ id: '1', body: [] }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/documents/tags/1');
    expect(req.request.method).toBe('PATCH');
    req.flush(mockBody);
  });

  it('bulkDeleteTags sends POST to /api/v1/documents/tags/bulk-delete and unwraps body', () => {
    const mockBody = { deletedCount: 2 };
    service.bulkDeleteTags({ body: { ids: ['1', '2'] } }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/documents/tags/bulk-delete');
    expect(req.request.method).toBe('POST');
    req.flush(mockBody);
  });
});
