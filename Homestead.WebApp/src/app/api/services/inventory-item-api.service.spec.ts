import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { InventoryItemApiService } from './inventory-item-api.service';

describe('InventoryItemApiService', () => {
  let service: InventoryItemApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(InventoryItemApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('getItem sends GET with path param', () => {
    const mockBody = { id: 'abc', name: 'Drill', kind: 'Durable' };
    service.getItem({ id: 'abc' }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/inventory/items/abc');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('getItemActivity sends GET with paging params and unwraps body', () => {
    const mockBody = { items: [], page: 2, pageSize: 20, totalCount: 0, totalPages: 0 };
    service
      .getItemActivity({ id: 'abc', Page: 2, PageSize: 20 })
      .subscribe(result => {
        expect(result).toEqual(mockBody);
      });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/inventory/items/abc/activity');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('Page')).toBe('2');
    expect(req.request.params.get('PageSize')).toBe('20');
    req.flush(mockBody);
  });

  it('createItemNote sends POST with the note body', () => {
    const mockBody = { id: 'n1', itemId: 'abc', body: 'Changed the oil' };
    service
      .createItemNote({ id: 'abc', body: { body: 'Changed the oil', occurredAt: null } })
      .subscribe(result => {
        expect(result).toEqual(mockBody);
      });
    const req = httpTesting.expectOne('/api/v1/inventory/items/abc/activity/notes');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ body: 'Changed the oil', occurredAt: null });
    req.flush(mockBody);
  });

  it('updateItemNote sends PUT with the noteId path param and body', () => {
    const mockBody = { id: 'n1', itemId: 'abc', body: 'Fixed' };
    service
      .updateItemNote({
        id: 'abc',
        noteId: 'n1',
        body: { body: 'Fixed', occurredAt: '2026-03-04T12:00:00.000Z' },
      })
      .subscribe(result => {
        expect(result).toEqual(mockBody);
      });
    const req = httpTesting.expectOne('/api/v1/inventory/items/abc/activity/notes/n1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ body: 'Fixed', occurredAt: '2026-03-04T12:00:00.000Z' });
    req.flush(mockBody);
  });

  it('deleteItemNote sends DELETE with the noteId path param', () => {
    service.deleteItemNote({ id: 'abc', noteId: 'n1' }).subscribe();
    const req = httpTesting.expectOne('/api/v1/inventory/items/abc/activity/notes/n1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('getItemWarranties sends GET and unwraps the list body', () => {
    const mockBody = [{ id: 'w1', itemId: 'abc', provider: 'Bosch', status: 'Active' }];
    service.getItemWarranties({ id: 'abc' }).subscribe(result => {
      expect(result).toEqual(mockBody);
    });
    const req = httpTesting.expectOne('/api/v1/inventory/items/abc/warranties');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('createItemWarranty sends POST with the warranty body', () => {
    const mockBody = { id: 'w1', itemId: 'abc', provider: null, expiresOn: null, notes: null, status: 'Active' };
    service
      .createItemWarranty({ id: 'abc', body: { provider: null, expiresOn: null, notes: null } })
      .subscribe(result => {
        expect(result).toEqual(mockBody);
      });
    const req = httpTesting.expectOne('/api/v1/inventory/items/abc/warranties');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ provider: null, expiresOn: null, notes: null });
    req.flush(mockBody);
  });

  it('updateItemWarranty sends PUT with the warrantyId path param and body', () => {
    const mockBody = { id: 'w1', itemId: 'abc', provider: 'Bosch', expiresOn: '2028-03-12', notes: null, status: 'Active' };
    service
      .updateItemWarranty({
        id: 'abc',
        warrantyId: 'w1',
        body: { provider: 'Bosch', expiresOn: '2028-03-12', notes: null },
      })
      .subscribe(result => {
        expect(result).toEqual(mockBody);
      });
    const req = httpTesting.expectOne('/api/v1/inventory/items/abc/warranties/w1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ provider: 'Bosch', expiresOn: '2028-03-12', notes: null });
    req.flush(mockBody);
  });

  it('deleteItemWarranty sends DELETE with the warrantyId path param', () => {
    service.deleteItemWarranty({ id: 'abc', warrantyId: 'w1' }).subscribe();
    const req = httpTesting.expectOne('/api/v1/inventory/items/abc/warranties/w1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
