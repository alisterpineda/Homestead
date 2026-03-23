import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { DocumentVersionApiService } from './document-version-api.service';

describe('DocumentVersionApiService', () => {
  let service: DocumentVersionApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(DocumentVersionApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('getDocumentVersion sends GET and unwraps body', () => {
    const mockBody = { id: 'v1', documentId: 'd1' };
    service.getDocumentVersion({ documentId: 'd1', id: 'v1' }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/documents/d1/versions/v1');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('patchDocumentVersion sends PATCH and unwraps body', () => {
    const mockBody = { id: 'v1' };
    service.patchDocumentVersion({ documentId: 'd1', id: 'v1', body: [] }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/documents/d1/versions/v1');
    expect(req.request.method).toBe('PATCH');
    req.flush(mockBody);
  });

  it('reprocessDocumentVersion sends POST and unwraps body', () => {
    const mockBody = { jobId: 'j1' };
    service.reprocessDocumentVersion({ documentId: 'd1', versionId: 'v1' }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/documents/d1/versions/v1/reprocess');
    expect(req.request.method).toBe('POST');
    req.flush(mockBody);
  });

  it('getDocumentActivity sends GET and unwraps body', () => {
    const mockBody = { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 };
    service.getDocumentActivity({ documentId: 'd1', Page: 1, PageSize: 10 }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/documents/d1/activity');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('createComment sends POST and unwraps body', () => {
    const mockBody = { id: 'c1' };
    service.createComment({ documentId: 'd1', body: { body: 'hello' } }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/documents/d1/comments');
    expect(req.request.method).toBe('POST');
    req.flush(mockBody);
  });

  it('getTextExtractionEngineSummaries sends GET and unwraps body', () => {
    const mockBody = [{ id: 'e1', name: 'Tesseract' }];
    service.getTextExtractionEngineSummaries().subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/documents/text-extraction-engines');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });
});
