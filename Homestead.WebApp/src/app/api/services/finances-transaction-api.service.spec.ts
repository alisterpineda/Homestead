import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { FinancesTransactionApiService } from './finances-transaction-api.service';

describe('FinancesTransactionApiService', () => {
  let service: FinancesTransactionApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    service = TestBed.inject(FinancesTransactionApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('getAccountTransactions sends GET with paging', () => {
    const mockBody = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 };
    service.getAccountTransactions({ accountId: 'acc-1', Page: 1, PageSize: 25 }).subscribe(r => {
      expect(r).toEqual(mockBody);
    });
    const req = httpTesting.expectOne(r => r.url === '/api/v1/finances/accounts/acc-1/transactions');
    expect(req.request.method).toBe('GET');
    req.flush(mockBody);
  });

  it('createTransfer sends POST', () => {
    const mockBody = { id: 'tx-1', shape: 'Transfer', splits: [], memo: '', occurredAt: '' };
    service.createTransfer({
      body: { sourceAccountId: 'a', destinationAccountId: 'b', sourceAmount: 100, date: '2026-04-23' },
    }).subscribe(r => expect(r).toEqual(mockBody));
    const req = httpTesting.expectOne('/api/v1/finances/transactions/transfers');
    expect(req.request.method).toBe('POST');
    req.flush(mockBody);
  });

  it('patchTransfer sends PATCH with path id', () => {
    const mockBody = { id: 'tx-1', shape: 'Transfer', splits: [], memo: '', occurredAt: '' };
    service.patchTransfer({
      transactionId: 'tx-1',
      body: { sourceAccountId: 'a', destinationAccountId: 'b', sourceAmount: 100, date: '2026-04-23' },
    }).subscribe(r => expect(r).toEqual(mockBody));
    const req = httpTesting.expectOne('/api/v1/finances/transactions/transfers/tx-1');
    expect(req.request.method).toBe('PATCH');
    req.flush(mockBody);
  });

  it('deleteTransaction sends DELETE', () => {
    service.deleteTransaction({ transactionId: 'tx-1' }).subscribe();
    const req = httpTesting.expectOne('/api/v1/finances/transactions/tx-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('createDrip sends POST to the drips group', () => {
    const mockBody = { id: 'tx-div', shape: 'Dividend', splits: [], memo: '', occurredAt: '', relatedTransactionId: 'tx-buy' };
    service.createDrip({
      body: { accountId: 'inv', securityId: 'vfv', paymentCurrencyId: 'usd', amount: 5, units: 0.05, occurredAt: '2026-04-23T12:00:00.000Z', description: null },
    }).subscribe(r => expect(r).toEqual(mockBody));
    const req = httpTesting.expectOne('/api/v1/finances/transactions/drips');
    expect(req.request.method).toBe('POST');
    req.flush(mockBody);
  });

  it('patchDrip sends PATCH with path id', () => {
    const mockBody = { id: 'tx-div', shape: 'Dividend', splits: [], memo: '', occurredAt: '', relatedTransactionId: 'tx-buy' };
    service.patchDrip({
      transactionId: 'tx-div',
      body: { accountId: 'inv', securityId: 'vfv', paymentCurrencyId: 'usd', amount: 6, units: 0.06, occurredAt: '2026-04-23T12:00:00.000Z', description: null },
    }).subscribe(r => expect(r).toEqual(mockBody));
    const req = httpTesting.expectOne('/api/v1/finances/transactions/drips/tx-div');
    expect(req.request.method).toBe('PATCH');
    req.flush(mockBody);
  });

  it('deleteDrip sends DELETE on the drips group', () => {
    service.deleteDrip({ transactionId: 'tx-div' }).subscribe();
    const req = httpTesting.expectOne('/api/v1/finances/transactions/drips/tx-div');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('createBalanceEntry sends POST under account', () => {
    const mockBody = { transactionId: 'tx', accountId: 'acc', commodityId: 'cad', assertedAmount: 10, date: '2026-04-23' };
    service.createBalanceEntry({
      accountId: 'acc',
      body: { commodityId: 'cad', assertedAmount: 10, date: '2026-04-23' },
    }).subscribe(r => expect(r).toEqual(mockBody));
    const req = httpTesting.expectOne('/api/v1/finances/accounts/acc/balance-entries');
    expect(req.request.method).toBe('POST');
    req.flush(mockBody);
  });

  it('patchBalanceEntry sends PATCH with composite path', () => {
    const mockBody = { transactionId: 'tx', accountId: 'acc', commodityId: 'cad', assertedAmount: 11, date: '2026-04-23' };
    service.patchBalanceEntry({
      accountId: 'acc',
      transactionId: 'tx',
      body: { commodityId: 'cad', assertedAmount: 11, date: '2026-04-23' },
    }).subscribe(r => expect(r).toEqual(mockBody));
    const req = httpTesting.expectOne('/api/v1/finances/accounts/acc/balance-entries/tx');
    expect(req.request.method).toBe('PATCH');
    req.flush(mockBody);
  });

  it('patchOpeningBalance uses commodityId path segment', () => {
    const mockBody = { transactionId: 'tx', accountId: 'acc', commodityId: 'cad', quantity: 1000, asOfDate: '2026-04-23' };
    service.patchOpeningBalance({
      accountId: 'acc',
      commodityId: 'cad',
      body: { quantity: 1000, asOfDate: '2026-04-23' },
    }).subscribe(r => expect(r).toEqual(mockBody));
    const req = httpTesting.expectOne('/api/v1/finances/accounts/acc/opening-balances/cad');
    expect(req.request.method).toBe('PATCH');
    req.flush(mockBody);
  });

  it('deleteOpeningBalance sends DELETE on (account, commodity)', () => {
    service.deleteOpeningBalance({ accountId: 'acc', commodityId: 'cad' }).subscribe();
    const req = httpTesting.expectOne('/api/v1/finances/accounts/acc/opening-balances/cad');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
