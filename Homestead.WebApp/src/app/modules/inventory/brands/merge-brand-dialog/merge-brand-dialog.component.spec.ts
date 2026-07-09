import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { MergeBrandDialogComponent } from './merge-brand-dialog.component';
import { InventoryBrandApiService } from '@/api/services/inventory-brand-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { BrandResponse } from '@/api/generated/models/brand-response';

const SOURCE: BrandResponse = {
  id: 'src', name: 'Apple Inc', itemCount: 2,
  createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-02T00:00:00Z',
};
const TARGET = { id: 'tgt', name: 'Apple' };

describe('MergeBrandDialogComponent', () => {
  let fixture: ComponentFixture<MergeBrandDialogComponent>;
  let component: MergeBrandDialogComponent;
  let mergeBrand: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    mergeBrand = vi.fn().mockReturnValue(of({ ...SOURCE, id: 'tgt', name: 'Apple', itemCount: 3 }));
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        { provide: InventoryBrandApiService, useValue: { mergeBrand, getBrands: () => of({ items: [], totalCount: 0 }) } },
        { provide: ErrorHandlerService, useValue: { getErrorMessage: (_e: unknown, f: string) => f } },
      ],
    });
    fixture = TestBed.createComponent(MergeBrandDialogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('source', SOURCE);
  });

  it('confirm calls mergeBrand with source→target and emits merged', async () => {
    const merged: BrandResponse[] = [];
    component.merged.subscribe(v => merged.push(v));

    component.target.set(TARGET);
    expect(component.canConfirm()).toBe(true);

    component.onConfirm();
    await new Promise(r => setTimeout(r));

    expect(mergeBrand).toHaveBeenCalledWith({ id: 'src', body: { targetId: 'tgt' } });
    expect(merged.length).toBe(1);
    expect(merged[0].id).toBe('tgt');
    expect(component.merging()).toBe(false);
  });

  it('cannot confirm without a chosen target', () => {
    expect(component.canConfirm()).toBe(false);
  });
});
