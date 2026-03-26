import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { ExportApiService } from './export-api.service';

describe('ExportApiService', () => {
  let service: ExportApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(ExportApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('exportDocuments sends POST and unwraps body', () => {
    const mockBody = { jobId: 'abc-123' };
    service.exportDocuments({ body: { ids: ['id-1', 'id-2'] } }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/documents/export');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ ids: ['id-1', 'id-2'] });
    req.flush(mockBody);
  });

  it('exportDocuments sends null ids for export all', () => {
    const mockBody = { jobId: 'abc-456' };
    service.exportDocuments({ body: { ids: null } }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/documents/export');
    expect(req.request.body).toEqual({ ids: null });
    req.flush(mockBody);
  });

  it('getExports sends GET with pagination and unwraps body', () => {
    const mockBody = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 };
    service.getExports({ Page: 1, PageSize: 20 }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url.startsWith('/api/v1/documents/export'));
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('getDownloadUrl returns correct URL', () => {
    const url = service.getDownloadUrl('job-123');
    expect(url).toBe('/api/v1/documents/export/job-123/download');
  });
});
