import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { DownloadApiService } from './download-api.service';

describe('DownloadApiService', () => {
  let service: DownloadApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(DownloadApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('downloadDocuments sends POST and unwraps body', () => {
    const mockBody = { jobId: 'abc-123' };
    service.downloadDocuments({ body: { ids: ['id-1', 'id-2'] } }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/documents/download');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ ids: ['id-1', 'id-2'] });
    req.flush(mockBody);
  });

  it('downloadDocuments sends null ids for download all', () => {
    const mockBody = { jobId: 'abc-456' };
    service.downloadDocuments({ body: { ids: null } }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/documents/download');
    expect(req.request.body).toEqual({ ids: null });
    req.flush(mockBody);
  });

  it('getDownloadUrl returns correct URL', () => {
    const url = service.getDownloadUrl('job-123');
    expect(url).toBe('/api/v1/documents/download/job-123');
  });
});
