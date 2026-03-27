import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { AdminApiService } from './admin-api.service';

describe('AdminApiService', () => {
  let service: AdminApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(AdminApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('getUsers sends GET and unwraps body', () => {
    const mockBody = { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 };
    service.getUsers({ Page: 1, PageSize: 10 }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/admin/users');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('createUser sends POST and unwraps body', () => {
    const mockBody = { id: '1', email: 'test@test.com' };
    service.createUser({ body: { email: 'test@test.com', displayName: 'Test', password: 'pass123', role: 'Viewer' } }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/admin/users');
    expect(req.request.method).toBe('POST');
    req.flush(mockBody);
  });

  it('getGroups sends GET and unwraps body', () => {
    const mockBody = { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 };
    service.getGroups({ Page: 1, PageSize: 10 }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/admin/groups');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('getGroup sends GET with path param and unwraps body', () => {
    const mockBody = { id: '1', name: 'Group A' };
    service.getGroup({ id: '1' }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/admin/groups/1');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('deleteGroup sends DELETE', () => {
    service.deleteGroup({ id: '1' }).subscribe();
    const req = httpTesting.expectOne('/api/v1/admin/groups/1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('getProcessingJobs sends GET and unwraps body', () => {
    const mockBody = { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 };
    service.getProcessingJobs({ Page: 1, PageSize: 10 }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/processing/jobs');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('getTextExtractionEngines sends GET and unwraps body', () => {
    const mockBody = { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 };
    service.getTextExtractionEngines({ Page: 1, PageSize: 10 }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/admin/text-extraction-engines');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });
});
