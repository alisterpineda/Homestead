import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { FinancesAccountApiService } from './finances-account-api.service';

describe('FinancesAccountApiService', () => {
  let service: FinancesAccountApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(FinancesAccountApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('getAccounts sends GET with AsOf query and unwraps body', () => {
    const mockBody = { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 };
    service.getAccounts({ Page: 1, PageSize: 50 }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/finances/accounts');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.has('AsOf')).toBe(true);
    req.flush(mockBody);
  });

  it('getAccount sends GET with path param and AsOf query', () => {
    const mockBody = { id: 'abc', name: 'Chequing' };
    service.getAccount({ id: 'abc' }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/finances/accounts/abc');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.has('AsOf')).toBe(true);
    req.flush(mockBody);
  });

  it('createAccount sends POST', () => {
    const mockBody = { id: 'abc', name: 'Chequing' };
    service.createAccount({
      body: { name: 'Chequing', kind: 'Cash', reportingCurrencyId: 'cad' },
    }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/finances/accounts');
    expect(req.request.method).toBe('POST');
    req.flush(mockBody);
  });

  it('patchAccount sends PATCH', () => {
    const mockBody = { id: 'abc', name: 'New name' };
    service.patchAccount({
      id: 'abc',
      body: [{ op: 'replace', path: '/name', value: 'New name' }],
    }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/finances/accounts/abc');
    expect(req.request.method).toBe('PATCH');
    req.flush(mockBody);
  });

  it('deleteAccount sends DELETE', () => {
    service.deleteAccount({ id: 'abc' }).subscribe();
    const req = httpTesting.expectOne('/api/v1/finances/accounts/abc');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('getAccountPositions sends GET', () => {
    const mockBody: unknown[] = [];
    service.getAccountPositions({ accountId: 'abc' }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/finances/accounts/abc/positions');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });
});
