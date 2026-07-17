import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { PayeeListComponent } from './payee-list.component';
import { FinancesPayeeApiService } from '@/api/services/finances-payee-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { AuthService } from '@/shared/services/auth.service';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import type { PayeeResponse } from '@/api/generated/models/payee-response';

// The rendered payee-table (TanStack) uses ResizeObserver, absent in jsdom; safe to stub.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

const PAYEE: PayeeResponse = {
  id: 'p1', name: 'Costco', isArchived: false, transactionCount: 0,
  createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-02T00:00:00Z',
};

const IN_USE_PAYEE: PayeeResponse = { ...PAYEE, id: 'p2', name: 'Safeway', transactionCount: 3 };

class StubPayeeApi {
  deleteFailure: unknown = null;
  deleteCalls: string[] = [];
  lastGetParams: { SortBy?: string; SortDir?: string; IncludeArchived?: boolean } | undefined;
  getPayees = (params?: { SortBy?: string; SortDir?: string; IncludeArchived?: boolean }) => {
    this.lastGetParams = params;
    return of({ items: [PAYEE], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 });
  };
  deletePayee = (p: { id: string }) => {
    this.deleteCalls.push(p.id);
    return this.deleteFailure ? throwError(() => this.deleteFailure) : of(undefined);
  };
}

// Extracts Problem Details `detail` (mirrors the real ErrorHandlerService well enough for this spec).
class StubErrorHandler {
  getErrorMessage = (err: unknown, fallback: string) =>
    (err as HttpErrorResponse | undefined)?.error?.detail ?? fallback;
}

describe('PayeeListComponent', () => {
  let fixture: ComponentFixture<PayeeListComponent>;
  let component: PayeeListComponent;
  let api: StubPayeeApi;

  beforeEach(() => {
    api = new StubPayeeApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: FinancesPayeeApiService, useValue: api },
        { provide: ErrorHandlerService, useValue: new StubErrorHandler() },
        { provide: AuthService, useValue: { canWrite: () => true } },
        { provide: UserPreferencesService, useValue: { pageSize: signal(50) } },
      ],
    });
    fixture = TestBed.createComponent(PayeeListComponent);
    component = fixture.componentInstance;
  });

  it('renders the loaded payees', async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Costco');
  });

  it('closes the dialog, clears the error, and refreshes on a successful delete', () => {
    const close = vi.fn();
    const before = component.refreshTrigger();
    component.onDeleteRequest(PAYEE);
    component.confirmDelete({ close });

    expect(api.deleteCalls).toEqual(['p1']);
    expect(close).toHaveBeenCalledTimes(1);
    expect(component.deleteError()).toBe('');
    expect(component.pendingDelete()).toBeNull();
    expect(component.deleting()).toBe(false);
    expect(component.refreshTrigger()).toBe(before + 1);
  });

  it('surfaces the in-use 409 rejection inline and keeps the dialog open on a failed delete', () => {
    const close = vi.fn();
    component.onDeleteRequest(IN_USE_PAYEE);
    expect(component.deleteState()).toBe('open');

    api.deleteFailure = new HttpErrorResponse({
      status: 409,
      error: { detail: 'Payee is used by one or more transactions and cannot be deleted. Archive it instead.' },
    });
    component.confirmDelete({ close });

    expect(component.deleteError()).toBe('Payee is used by one or more transactions and cannot be deleted. Archive it instead.');
    expect(close).not.toHaveBeenCalled();
    expect(component.deleteState()).toBe('open');
    expect(component.pendingDelete()).toEqual(IN_USE_PAYEE);
    expect(component.deleting()).toBe(false);
  });

  it('clears any prior error when the delete dialog is reopened', () => {
    component.deleteError.set('stale reason');
    component.onDeleteRequest(PAYEE);
    expect(component.deleteError()).toBe('');
  });

  it('sends the effective sort direction for a deep-linked sortBy with no sortDir', async () => {
    const router = TestBed.inject(Router);
    fixture.detectChanges();
    await fixture.whenStable();

    await router.navigate([], { queryParams: { sortBy: 'transactions' } });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.lastGetParams?.SortBy).toBe('transactions');
    expect(api.lastGetParams?.SortDir).toBe('desc');
  });

  it('passes IncludeArchived through when the query param is set', async () => {
    const router = TestBed.inject(Router);
    fixture.detectChanges();
    await fixture.whenStable();

    await router.navigate([], { queryParams: { includeArchived: true } });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.lastGetParams?.IncludeArchived).toBe(true);
  });

  it('opens and closes the edit dialog', () => {
    component.onEdit(PAYEE);
    expect(component.editState()).toBe('open');
    expect(component.selectedPayee()).toEqual(PAYEE);

    component.onEditClosed();
    expect(component.editState()).toBe('closed');
    expect(component.selectedPayee()).toBeNull();
  });
});
