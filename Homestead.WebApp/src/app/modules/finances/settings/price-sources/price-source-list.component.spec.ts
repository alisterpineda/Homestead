import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { PriceSourceListComponent } from './price-source-list.component';
import { FinancesPriceSourcesApiService } from '@/api/services/finances-price-sources-api.service';
import type { GetPriceSourcesResponse } from '@/api/generated/models/get-price-sources-response';
import type { PriceSourceSummary } from '@/api/generated/models/price-source-summary';

class FakeApi {
  list() {
    return of<GetPriceSourcesResponse>({
      items: [
        { source: 'Transaction', displayName: 'Transaction', kind: 'Transaction', isEnabled: true, priceCount: 0 },
        { source: 'Manual', displayName: 'Manual', kind: 'Manual', isEnabled: true, priceCount: 5 },
        { source: 'BankOfCanada', displayName: 'Bank of Canada', kind: 'External', isEnabled: false, priceCount: 0 },
      ],
    });
  }
}

describe('PriceSourceListComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: FinancesPriceSourcesApiService, useClass: FakeApi },
      ],
    });
  });

  it('renders the three sources', () => {
    const fixture = TestBed.createComponent(PriceSourceListComponent);
    fixture.detectChanges();
    expect(fixture.componentInstance.data()?.items.length).toBe(3);
  });

  it('maps Failed status to destructive variant', () => {
    const fixture = TestBed.createComponent(PriceSourceListComponent);
    const item: PriceSourceSummary = {
      source: 'BankOfCanada', displayName: 'Bank of Canada', kind: 'External',
      isEnabled: true, priceCount: 0,
      lastImportAt: '2026-04-28T10:00:00Z',
      lastImportStatus: 'Failed',
    };
    expect(fixture.componentInstance.statusPill(item)).toEqual({ label: 'Failed', variant: 'destructive' });
  });

  it('maps Success status to default variant', () => {
    const fixture = TestBed.createComponent(PriceSourceListComponent);
    const item: PriceSourceSummary = {
      source: 'BankOfCanada', displayName: 'Bank of Canada', kind: 'External',
      isEnabled: true, priceCount: 0,
      lastImportAt: '2026-04-28T10:00:00Z',
      lastImportStatus: 'Success',
    };
    expect(fixture.componentInstance.statusPill(item)).toEqual({ label: 'Success', variant: 'default' });
  });

  it('maps null lastImportAt to Never run', () => {
    const fixture = TestBed.createComponent(PriceSourceListComponent);
    const item: PriceSourceSummary = {
      source: 'BankOfCanada', displayName: 'Bank of Canada', kind: 'External',
      isEnabled: true, priceCount: 0,
    };
    expect(fixture.componentInstance.statusPill(item)).toEqual({ label: 'Never run', variant: 'outline' });
  });
});
