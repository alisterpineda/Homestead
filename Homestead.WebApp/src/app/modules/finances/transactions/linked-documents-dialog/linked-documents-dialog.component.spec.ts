import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { LinkedDocumentsDialogComponent } from './linked-documents-dialog.component';
import { FinancesTransactionApiService } from '@/api/services/finances-transaction-api.service';
import { DocumentApiService } from '@/api/services/document-api.service';
import type { TransactionDocumentLinkResponse } from '@/api/generated/models/transaction-document-link-response';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

function makeLink(overrides: Partial<TransactionDocumentLinkResponse> & { documentId: string }): TransactionDocumentLinkResponse {
  return {
    documentName: 'Receipt',
    availability: 'Available',
    origin: 'Manual',
    createdAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

describe('LinkedDocumentsDialogComponent', () => {
  let txApi: {
    getTransactionDocuments: ReturnType<typeof vi.fn>;
    addTransactionDocumentLink: ReturnType<typeof vi.fn>;
    removeTransactionDocumentLink: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    txApi = {
      getTransactionDocuments: vi.fn(() => of([] as TransactionDocumentLinkResponse[])),
      addTransactionDocumentLink: vi.fn(() => of(makeLink({ documentId: 'doc-9' }))),
      removeTransactionDocumentLink: vi.fn(() => of(undefined)),
    };
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        { provide: FinancesTransactionApiService, useValue: txApi },
        // Embedded document picker searches through this; never called while search is empty.
        { provide: DocumentApiService, useValue: { getDocuments: vi.fn(() => of({ items: [] })) } },
      ],
    });
  });

  async function setup(transactionId: string | null = 'tx-1') {
    const fixture = TestBed.createComponent(LinkedDocumentsDialogComponent);
    if (transactionId !== null) {
      fixture.componentRef.setInput('transactionId', transactionId);
    }
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  it('fetches links for the transaction and exposes them (origin/availability included)', async () => {
    const links = [
      makeLink({ documentId: 'doc-1' }),
      makeLink({ documentId: 'doc-2', origin: 'StatementImport', availability: 'Trashed' }),
    ];
    txApi.getTransactionDocuments.mockReturnValue(of(links));

    const fixture = await setup('tx-1');
    const c = fixture.componentInstance;

    expect(txApi.getTransactionDocuments).toHaveBeenCalledWith({ transactionId: 'tx-1' });
    expect(c.links()).toEqual(links);
    expect(c.loading()).toBe(false);
  });

  it('does not fetch while transactionId is null', async () => {
    const fixture = await setup(null);
    expect(txApi.getTransactionDocuments).not.toHaveBeenCalled();
    expect(fixture.componentInstance.links()).toEqual([]);
  });

  it('onAdd links the picked document and emits linksChanged', async () => {
    const fixture = await setup('tx-1');
    const c = fixture.componentInstance;
    let changed = 0;
    c.linksChanged.subscribe(() => changed++);
    c.search.set('rec');
    txApi.getTransactionDocuments.mockClear();

    c.onAdd({ id: 'doc-9', name: 'Receipt' } as GetDocumentsResponse);

    expect(txApi.addTransactionDocumentLink).toHaveBeenCalledWith({
      transactionId: 'tx-1',
      body: { documentId: 'doc-9' },
    });
    expect(changed).toBe(1);
    expect(c.adding()).toBe(false);
    // Search resets and the link list re-fetches so the new row appears.
    expect(c.search()).toBe('');
    await fixture.whenStable();
    expect(txApi.getTransactionDocuments).toHaveBeenCalledWith({ transactionId: 'tx-1' });
  });

  it('onRemove unlinks the document and emits linksChanged', async () => {
    const fixture = await setup('tx-1');
    const c = fixture.componentInstance;
    let changed = 0;
    c.linksChanged.subscribe(() => changed++);
    txApi.getTransactionDocuments.mockClear();

    c.onRemove(makeLink({ documentId: 'doc-1' }));

    expect(txApi.removeTransactionDocumentLink).toHaveBeenCalledWith({
      transactionId: 'tx-1',
      documentId: 'doc-1',
    });
    expect(changed).toBe(1);
    expect(c.removing()).toBe(false);
    await fixture.whenStable();
    expect(txApi.getTransactionDocuments).toHaveBeenCalledWith({ transactionId: 'tx-1' });
  });

  it('onAdd and onRemove no-op when transactionId is null', async () => {
    const fixture = await setup(null);
    const c = fixture.componentInstance;
    let changed = 0;
    c.linksChanged.subscribe(() => changed++);

    c.onAdd({ id: 'doc-9', name: 'Receipt' } as GetDocumentsResponse);
    c.onRemove(makeLink({ documentId: 'doc-1' }));

    expect(txApi.addTransactionDocumentLink).not.toHaveBeenCalled();
    expect(txApi.removeTransactionDocumentLink).not.toHaveBeenCalled();
    expect(changed).toBe(0);
  });

  it('opening resets the search term without issuing an extra fetch (the id change fetches)', async () => {
    const fixture = await setup('tx-1');
    const c = fixture.componentInstance;
    c.search.set('leftover term');
    txApi.getTransactionDocuments.mockClear();

    c.onStateChanged('open');

    expect(c.search()).toBe('');
    await fixture.whenStable();
    // The host clears transactionId on close, so every open arrives as an id change and the
    // combineLatest fetches exactly once — an on-open bump here would double-fetch.
    expect(txApi.getTransactionDocuments).not.toHaveBeenCalled();
  });

  it('closing emits closed without re-fetching', async () => {
    const fixture = await setup('tx-1');
    const c = fixture.componentInstance;
    let closedCount = 0;
    c.closed.subscribe(() => closedCount++);
    txApi.getTransactionDocuments.mockClear();

    c.onStateChanged('closed');

    expect(closedCount).toBe(1);
    await fixture.whenStable();
    expect(txApi.getTransactionDocuments).not.toHaveBeenCalled();
  });
});
