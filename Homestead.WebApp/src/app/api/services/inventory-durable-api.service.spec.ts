import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { InventoryDurableApiService } from './inventory-durable-api.service';

describe('InventoryDurableApiService', () => {
  let service: InventoryDurableApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(InventoryDurableApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('getDurables sends GET and unwraps body', () => {
    const mockBody = { items: [], page: 1, pageSize: 200, totalCount: 0, totalPages: 0 };
    service.getDurables({ Page: 1, PageSize: 200, IncludeInactive: true }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/inventory/durables');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('IncludeInactive')).toBe('true');
    req.flush(mockBody);
  });

  it('getDurable sends GET with path param', () => {
    const mockBody = { id: 'abc', name: 'Drill' };
    service.getDurable({ id: 'abc' }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/inventory/durables/abc');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('getDurableActivity sends GET with paging params and unwraps body', () => {
    const mockBody = { items: [], page: 2, pageSize: 25, totalCount: 0, totalPages: 0 };
    service.getDurableActivity({ id: 'abc', Page: 2, PageSize: 25 }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/inventory/durables/abc/activity');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('Page')).toBe('2');
    expect(req.request.params.get('PageSize')).toBe('25');
    req.flush(mockBody);
  });

  it('createDurable sends POST', () => {
    const mockBody = { id: 'abc', name: 'Drill' };
    service.createDurable({ body: { name: 'Drill' } }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/inventory/durables');
    expect(req.request.method).toBe('POST');
    req.flush(mockBody);
  });

  it('patchDurable sends PATCH', () => {
    const mockBody = { id: 'abc', name: 'Renamed' };
    service.patchDurable({
      id: 'abc',
      body: [{ op: 'replace', path: '/name', value: 'Renamed' }],
    }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/inventory/durables/abc');
    expect(req.request.method).toBe('PATCH');
    req.flush(mockBody);
  });

  it('deleteDurable sends DELETE', () => {
    service.deleteDurable({ id: 'abc' }).subscribe();
    const req = httpTesting.expectOne('/api/v1/inventory/durables/abc');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
