import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { PriceSourceDetailComponent } from './price-source-detail.component';
import { FinancesPriceSourcesApiService } from '@/api/services/finances-price-sources-api.service';
import type { GetPriceSourceResponse } from '@/api/generated/models/get-price-source-response';

class FakeApi {
  get() {
    return of<GetPriceSourceResponse>({
      source: 'BankOfCanada',
      displayName: 'Bank of Canada',
      kind: 'External',
      isEnabled: false,
      configSchema: { fields: [] },
      supportedPairs: [],
      priceCount: 0,
    });
  }
  patch() { return of({ source: 'BankOfCanada', isEnabled: true }); }
  upsertSchedule() { return of({ id: 'abc', nextRunAt: '2026-04-29T00:00:00Z' }); }
  deleteSchedule() { return of(undefined); }
  runNow() { return of({ jobId: 'xyz' }); }
}

describe('PriceSourceDetailComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: FinancesPriceSourcesApiService, useClass: FakeApi },
      ],
    });
  });

  it('loads detail for the source', () => {
    const fixture = TestBed.createComponent(PriceSourceDetailComponent);
    fixture.detectChanges();
    expect(fixture.componentInstance.data()).not.toBeNull();
    expect(fixture.componentInstance.data()!.source).toBe('BankOfCanada');
  });

  it('initialises isEnabled from response', () => {
    const fixture = TestBed.createComponent(PriceSourceDetailComponent);
    fixture.detectChanges();
    expect(fixture.componentInstance.isEnabled()).toBe(false);
  });
});
