import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { BrandListComponent } from './brand-list.component';
import { InventoryBrandApiService } from '@/api/services/inventory-brand-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { AuthService } from '@/shared/services/auth.service';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import type { BrandResponse } from '@/api/generated/models/brand-response';

// The rendered brand-table (TanStack) uses ResizeObserver, absent in jsdom; safe to stub.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

const BRAND: BrandResponse = {
  id: 'b1', name: 'Apple', itemCount: 0,
  createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-02T00:00:00Z',
};

const IN_USE_BRAND: BrandResponse = { ...BRAND, id: 'b2', name: 'Sony', itemCount: 3 };

class StubBrandApi {
  deleteFailure: unknown = null;
  deleteCalls: string[] = [];
  lastGetParams: { SortBy?: string; SortDir?: string } | undefined;
  getBrands = (params?: { SortBy?: string; SortDir?: string }) => {
    this.lastGetParams = params;
    return of({ items: [BRAND], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 });
  };
  deleteBrand = (p: { id: string }) => {
    this.deleteCalls.push(p.id);
    return this.deleteFailure ? throwError(() => this.deleteFailure) : of(undefined);
  };
}

// Extracts Problem Details `detail` (mirrors the real ErrorHandlerService well enough for this spec).
class StubErrorHandler {
  getErrorMessage = (err: unknown, fallback: string) =>
    (err as HttpErrorResponse | undefined)?.error?.detail ?? fallback;
}

describe('BrandListComponent', () => {
  let fixture: ComponentFixture<BrandListComponent>;
  let component: BrandListComponent;
  let api: StubBrandApi;

  beforeEach(() => {
    api = new StubBrandApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: InventoryBrandApiService, useValue: api },
        { provide: ErrorHandlerService, useValue: new StubErrorHandler() },
        { provide: AuthService, useValue: { canWrite: () => true } },
        { provide: UserPreferencesService, useValue: { pageSize: signal(50) } },
      ],
    });
    fixture = TestBed.createComponent(BrandListComponent);
    component = fixture.componentInstance;
  });

  it('closes the dialog, clears the error, and refreshes on a successful delete', () => {
    const close = vi.fn();
    const before = component.refreshTrigger();
    component.onDeleteRequest(BRAND);
    component.confirmDelete({ close });

    expect(api.deleteCalls).toEqual(['b1']);
    expect(close).toHaveBeenCalledTimes(1);
    expect(component.deleteError()).toBe('');
    expect(component.pendingDelete()).toBeNull();
    expect(component.deleting()).toBe(false);
    expect(component.refreshTrigger()).toBe(before + 1);
  });

  it('surfaces the in-use rejection inline and keeps the dialog open on a failed delete', () => {
    const close = vi.fn();
    component.onDeleteRequest(IN_USE_BRAND);
    expect(component.deleteState()).toBe('open');

    api.deleteFailure = new HttpErrorResponse({
      status: 400,
      error: { detail: 'Brand is in use by one or more items and cannot be deleted.' },
    });
    component.confirmDelete({ close });

    expect(component.deleteError()).toBe('Brand is in use by one or more items and cannot be deleted.');
    expect(close).not.toHaveBeenCalled();
    expect(component.deleteState()).toBe('open');
    expect(component.pendingDelete()).toEqual(IN_USE_BRAND);
    expect(component.deleting()).toBe(false);
  });

  it('clears any prior error when the delete dialog is reopened', () => {
    component.deleteError.set('stale reason');
    component.onDeleteRequest(BRAND);
    expect(component.deleteError()).toBe('');
  });

  it('opens the merge dialog and refreshes after a successful merge', () => {
    const before = component.refreshTrigger();
    component.onMergeRequest(IN_USE_BRAND);
    expect(component.mergeState()).toBe('open');
    expect(component.mergeSource()).toEqual(IN_USE_BRAND);
    expect(component.mergeTargetName()).toBe('');

    component.onMerged();
    expect(component.mergeState()).toBe('closed');
    expect(component.mergeSource()).toBeNull();
    expect(component.refreshTrigger()).toBe(before + 1);
  });

  it('sends the effective sort direction for a deep-linked sortBy with no sortDir', async () => {
    // Regression guard: a bookmarked ?sortBy=items (no sortDir) must request the per-field default
    // direction the toolbar/header display (desc), not the raw absent param (which the backend would
    // treat as asc) — otherwise the sort arrows lie about the actual order.
    const router = TestBed.inject(Router);
    fixture.detectChanges();
    await fixture.whenStable();

    await router.navigate([], { queryParams: { sortBy: 'items' } });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.lastGetParams?.SortBy).toBe('items');
    expect(api.lastGetParams?.SortDir).toBe('desc');
  });

  it('hands the rename-409 off to the merge dialog pre-seeded to the attempted name', () => {
    component.onEdit(BRAND);
    expect(component.editState()).toBe('open');

    component.onMergeFromRename({ source: BRAND, targetName: 'Apple Inc' });

    // The edit dialog closes; the merge dialog opens targeting the source, pre-seeded with the name.
    expect(component.editState()).toBe('closed');
    expect(component.selectedBrand()).toBeNull();
    expect(component.mergeState()).toBe('open');
    expect(component.mergeSource()).toEqual(BRAND);
    expect(component.mergeTargetName()).toBe('Apple Inc');
  });
});
