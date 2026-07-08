import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AreaListPageComponent } from './area-list-page.component';
import { InventoryAreaApiService } from '@/api/services/inventory-area-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { AreaResponse } from '@/api/generated/models/area-response';

const AREA: AreaResponse = {
  id: 'a1', name: 'Garage', liveItemCount: 2, createdAt: '2026-01-01T00:00:00Z',
} as unknown as AreaResponse;

class StubAreaApi {
  deleteFailure: unknown = null;
  deleteCalls: string[] = [];
  getAreas = () => of({ items: [AREA], page: 1, pageSize: 200, totalCount: 1, totalPages: 1 });
  deleteArea = (p: { id: string }) => {
    this.deleteCalls.push(p.id);
    return this.deleteFailure ? throwError(() => this.deleteFailure) : of(undefined);
  };
}

// Extracts Problem Details `detail` (mirrors the real ErrorHandlerService well enough for this spec).
class StubErrorHandler {
  getErrorMessage = (err: unknown, fallback: string) =>
    (err as HttpErrorResponse | undefined)?.error?.detail ?? fallback;
}

describe('AreaListPageComponent', () => {
  let fixture: ComponentFixture<AreaListPageComponent>;
  let component: AreaListPageComponent;
  let api: StubAreaApi;

  beforeEach(() => {
    api = new StubAreaApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: InventoryAreaApiService, useValue: api },
        { provide: ErrorHandlerService, useValue: new StubErrorHandler() },
      ],
    });
    fixture = TestBed.createComponent(AreaListPageComponent);
    component = fixture.componentInstance;
  });

  it('surfaces the rejection reason inline and keeps the dialog open on a failed delete', () => {
    const close = vi.fn();
    component.onDeleteRequest(AREA);
    expect(component.deleteError()).toBe('');
    expect(component.deleteState()).toBe('open');

    api.deleteFailure = new HttpErrorResponse({
      status: 409,
      error: { detail: 'Area has sub-areas or items — move or remove those first.' },
    });
    component.confirmDelete({ close });

    // The reason shows inline, the dialog stays open (close never called), the guard resets.
    expect(component.deleteError()).toBe('Area has sub-areas or items — move or remove those first.');
    expect(close).not.toHaveBeenCalled();
    expect(component.deleteState()).toBe('open');
    expect(component.pendingDelete()).toEqual(AREA);
    expect(component.deleting()).toBe(false);
  });

  it('clears any prior error when the delete dialog is reopened', () => {
    component.deleteError.set('stale reason');
    component.onDeleteRequest(AREA);
    expect(component.deleteError()).toBe('');
  });

  it('closes the dialog and clears the error on a successful delete', () => {
    const close = vi.fn();
    component.onDeleteRequest(AREA);
    component.confirmDelete({ close });

    expect(api.deleteCalls).toEqual(['a1']);
    expect(close).toHaveBeenCalledTimes(1);
    expect(component.deleteError()).toBe('');
    expect(component.pendingDelete()).toBeNull();
    expect(component.deleting()).toBe(false);
  });
});
