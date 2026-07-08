import { Component, input, output, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, throwError } from 'rxjs';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { CommodityDetailPageComponent } from './commodity-detail-page.component';
import { CommodityPricesSectionComponent } from './commodity-prices-section.component';
import { EditCommoditySheetComponent } from './edit-commodity-sheet/edit-commodity-sheet.component';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

// Light stand-ins so the spec exercises only the detail page's load/error logic, not the children's
// own data fetching. Same selectors so the page template binds to them transparently.
@Component({ selector: 'app-commodity-prices-section', template: '' })
class StubPricesSection {
  commodity = input<CommodityResponse | null>(null);
}

@Component({ selector: 'app-edit-commodity-sheet', template: '' })
class StubEditSheet {
  commodity = input<CommodityResponse | null>(null);
  state = input<BrnDialogState>('closed');
  saved = output<CommodityResponse>();
  closed = output<void>();
}

const COMMODITY: CommodityResponse = {
  id: 'aapl',
  symbol: 'AAPL',
  name: 'Apple Inc.',
  kind: 'Equity',
  isSystemManaged: false,
} as unknown as CommodityResponse;

class StubCommodityApi {
  commodity: CommodityResponse = COMMODITY;
  getError = false;
  getErrorStatus = 404; // 404 → not-found; anything else → retryable loadError
  getCalls = 0;
  getCommodity = (_p: { id: string }) => {
    this.getCalls++;
    return this.getError
      ? throwError(() => new HttpErrorResponse({ status: this.getErrorStatus }))
      : of(this.commodity);
  };
}

describe('CommodityDetailPageComponent', () => {
  let api: StubCommodityApi;

  beforeEach(() => {
    api = new StubCommodityApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'finances/commodities/:id', component: CommodityDetailPageComponent, data: { title: 'Commodity' } },
        ]),
        { provide: FinancesCommodityApiService, useValue: api },
      ],
    });
    TestBed.overrideComponent(CommodityDetailPageComponent, {
      remove: { imports: [CommodityPricesSectionComponent, EditCommoditySheetComponent] },
      add: { imports: [StubPricesSection, StubEditSheet] },
    });
  });

  async function navigate(url: string): Promise<{ harness: RouterTestingHarness; component: CommodityDetailPageComponent }> {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(url, CommodityDetailPageComponent);
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
    return { harness, component };
  }

  function text(harness: RouterTestingHarness): string {
    return (harness.routeNativeElement as HTMLElement).textContent ?? '';
  }

  function buttons(harness: RouterTestingHarness): HTMLButtonElement[] {
    return Array.from((harness.routeNativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'));
  }

  it('renders the commodity when loaded', async () => {
    const { harness } = await navigate('/finances/commodities/aapl');
    expect(text(harness)).toContain('AAPL');
    expect(text(harness)).toContain('Apple Inc.');
  });

  it('shows the not-found state on a 404', async () => {
    api.getError = true;
    api.getErrorStatus = 404;
    const { harness, component } = await navigate('/finances/commodities/missing');

    expect(component.notFound()).toBe(true);
    expect(component.loadError()).toBe(false);
    expect(text(harness)).toContain('Commodity not found');
  });

  it('shows a retryable error state (not "not found") on a non-404 load failure, and Retry re-fetches', async () => {
    api.getError = true;
    api.getErrorStatus = 500;
    const { harness, component } = await navigate('/finances/commodities/aapl');

    expect(component.notFound()).toBe(false);
    expect(component.loadError()).toBe(true);
    expect(text(harness)).toContain("Couldn't load commodity");
    expect(text(harness)).not.toContain('Commodity not found');

    api.getError = false;
    buttons(harness).find(b => b.textContent?.trim() === 'Retry')!.click();
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(api.getCalls).toBe(2);
    expect(text(harness)).toContain('AAPL');
    expect(component.loadError()).toBe(false);
  });

  it('keeps the rendered page and shows a banner when a refetch fails (non-404)', async () => {
    const { harness, component } = await navigate('/finances/commodities/aapl');
    expect(text(harness)).toContain('AAPL');

    api.getError = true;
    api.getErrorStatus = 500;
    component.onSheetSaved(); // a mutation bumps refreshTrigger → refetch fails
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(component.commodity()).not.toBeNull();
    expect(text(harness)).toContain('AAPL'); // last-loaded page preserved
    expect(component.loadError()).toBe(true);
    expect(text(harness)).toContain("Couldn't refresh");
    expect(text(harness)).not.toContain('Commodity not found');
  });
});
