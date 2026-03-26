import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { DocumentApiService } from './document-api.service';

describe('DocumentApiService', () => {
  let service: DocumentApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(DocumentApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('getDocuments sends GET and unwraps body', () => {
    const mockBody = { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 };
    service.getDocuments({ Page: 1, PageSize: 10, IncludeChildTags: false }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/documents');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('getDocument sends GET with path param and unwraps body', () => {
    const mockBody = { id: '1', name: 'Doc' };
    service.getDocument({ id: '1' }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/documents/1');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('patchDocument sends PATCH and unwraps body', () => {
    const mockBody = { id: '1' };
    service.patchDocument({ id: '1', body: [] }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/documents/1');
    expect(req.request.method).toBe('PATCH');
    req.flush(mockBody);
  });

  it('deleteDocument sends DELETE', () => {
    service.deleteDocument({ id: '1' }).subscribe();
    const req = httpTesting.expectOne('/api/v1/documents/1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('bulkDeleteDocuments sends POST and unwraps body', () => {
    const mockBody = { deletedCount: 2 };
    service.bulkDeleteDocuments({ body: { ids: ['1', '2'] } }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/documents/bulk-delete');
    expect(req.request.method).toBe('POST');
    req.flush(mockBody);
  });

  it('emptyTrash sends POST', () => {
    const mockBody = { deletedCount: 5 };
    service.emptyTrash().subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/documents/empty-trash');
    expect(req.request.method).toBe('POST');
    req.flush(mockBody);
  });

  it('getMyGroups sends GET and unwraps body', () => {
    const mockBody = [{ id: '1', name: 'Group A' }];
    service.getMyGroups().subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/documents/groups');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('getDocumentFile sends GET with blob responseType', () => {
    const mockBlob = new Blob(['file content'], { type: 'application/pdf' });
    service.getDocumentFile('doc-1').subscribe(result => {
      expect(result).toBeInstanceOf(Blob);
    });
    const req = httpTesting.expectOne('/api/v1/documents/doc-1/file');
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    req.flush(mockBlob);
  });

  it('getDocumentFile appends versionId query param when provided', () => {
    service.getDocumentFile('doc-1', 'ver-2').subscribe();
    const req = httpTesting.expectOne('/api/v1/documents/doc-1/file?versionId=ver-2');
    expect(req.request.method).toBe('GET');
    req.flush(new Blob());
  });

  it('getDocumentFile omits versionId when null', () => {
    service.getDocumentFile('doc-1', null).subscribe();
    const req = httpTesting.expectOne('/api/v1/documents/doc-1/file');
    expect(req.request.method).toBe('GET');
    req.flush(new Blob());
  });
});
