import { ChangeDetectionStrategy, Component, inject, input, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NgTemplateOutlet } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, throwError, Subject } from 'rxjs';
import { ItemDetailPageComponent } from './item-detail-page.component';
import { DurableDetailPageComponent } from '../../durables/durable-detail-page/durable-detail-page.component';
import { ConsumableDetailPageComponent } from '../../consumables/consumable-detail-page.component';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { PageHeaderService } from '@/shared/services/page-header.service';
import type { ItemDetailResponse } from '@/api/generated/models/item-detail-response';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableDetailResponse } from '@/api/generated/models/consumable-detail-response';

// Lightweight stand-ins for the real detail views, swapped in so the dispatcher spec exercises only
// the dispatch + loading/not-found logic — not the children's own data fetching. Same selectors as the
// real components, so the dispatcher template binds to them transparently.
@Component({
  selector: 'app-durable-detail-page',
  template: `<div>durable child: {{ preloaded()?.name }}</div>`,
})
class StubDurableDetailPageComponent {
  preloaded = input<DurableResponse | null>(null);
}

@Component({
  selector: 'app-consumable-detail-page',
  template: `<div>consumable child: {{ preloaded()?.consumable?.name }}</div>`,
})
class StubConsumableDetailPageComponent {
  preloaded = input<ConsumableDetailResponse | null>(null);
}

/**
 * Renders whatever template the dispatcher registered as the header title — the shared `Items › name`
 * breadcrumb lives in the shell header (via PageHeaderService), not in the dispatcher's own DOM, so
 * this stands in for the shell.
 */
@Component({
  selector: 'test-title-host',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet],
  template: `<ng-container [ngTemplateOutlet]="header.title()" />`,
})
class TitleHostComponent {
  protected header = inject(PageHeaderService);
}

const DURABLE_ITEM: ItemDetailResponse = {
  id: 'd1',
  fungibility: 'NonFungible',
  durable: {
    id: 'd1', name: 'Cordless Drill', fungibility: 'NonFungible', status: 'Active',
    isContainer: false, path: [], createdAt: '', updatedAt: '',
  },
};

const CONSUMABLE_ITEM: ItemDetailResponse = {
  id: 'c1',
  fungibility: 'Fungible',
  consumable: {
    consumable: {
      id: 'c1', name: 'Wood Screws', status: 'Active', unit: 'screws',
      totalOnHand: 0, isLow: false, createdAt: '', updatedAt: '',
    },
    locations: [],
  },
};

class StubItemApi {
  result = of<ItemDetailResponse>(DURABLE_ITEM);
  getItemCalls = 0;
  getItem = (_params: { id: string }) => {
    this.getItemCalls++;
    return this.result;
  };
}

describe('ItemDetailPageComponent', () => {
  let itemApi: StubItemApi;

  beforeEach(() => {
    itemApi = new StubItemApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'inventory/items/:id', component: ItemDetailPageComponent, data: { title: 'Item' } },
        ]),
        { provide: InventoryItemApiService, useValue: itemApi },
      ],
    });
    TestBed.overrideComponent(ItemDetailPageComponent, {
      remove: { imports: [DurableDetailPageComponent, ConsumableDetailPageComponent] },
      add: { imports: [StubDurableDetailPageComponent, StubConsumableDetailPageComponent] },
    });
  });

  async function navigate(url: string): Promise<{ harness: RouterTestingHarness; component: ItemDetailPageComponent }> {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(url, ItemDetailPageComponent);
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
    return { harness, component };
  }

  function text(harness: RouterTestingHarness): string {
    return (harness.routeNativeElement as HTMLElement).textContent ?? '';
  }

  it('shows the central spinner while the item is loading', async () => {
    itemApi.result = new Subject<ItemDetailResponse>(); // never emits → stays loading
    const { harness } = await navigate('/inventory/items/d1');

    expect((harness.routeNativeElement as HTMLElement).querySelector('hlm-spinner')).not.toBeNull();
    expect(text(harness)).not.toContain('child:');
  });

  it('shows the not-found state when the item is missing', async () => {
    itemApi.result = throwError(() => new Error('not found'));
    const { harness, component } = await navigate('/inventory/items/missing');

    expect(component.notFound()).toBe(true);
    expect(text(harness)).toContain('Item not found');
  });

  it('renders the durable child, preloaded, for a NonFungible item', async () => {
    itemApi.result = of(DURABLE_ITEM);
    const { harness } = await navigate('/inventory/items/d1');

    expect(itemApi.getItemCalls).toBe(1);
    const content = text(harness);
    expect(content).toContain('durable child: Cordless Drill');
    expect(content).not.toContain('consumable child');
  });

  it('renders the consumable child, preloaded, for a Fungible item', async () => {
    itemApi.result = of(CONSUMABLE_ITEM);
    const { harness } = await navigate('/inventory/items/c1');

    expect(itemApi.getItemCalls).toBe(1);
    const content = text(harness);
    expect(content).toContain('consumable child: Wood Screws');
    expect(content).not.toContain('durable child');
  });

  // The breadcrumb lives on the dispatcher (not the kind-specific children) so it stays flicker-free
  // across :id navigations. It's registered as the header title and outlet'd by the shell, not the body.
  async function renderTitle(): Promise<HTMLElement> {
    const host = TestBed.createComponent(TitleHostComponent);
    host.detectChanges();
    await host.whenStable();
    host.detectChanges();
    return host.nativeElement as HTMLElement;
  }

  it('registers the header breadcrumb via PageHeaderService once the item resolves', async () => {
    itemApi.result = of(DURABLE_ITEM);
    await navigate('/inventory/items/d1');

    expect(TestBed.inject(PageHeaderService).title()).not.toBeNull();
  });

  it('renders the registered breadcrumb with the durable name and an Items back-link', async () => {
    itemApi.result = of(DURABLE_ITEM);
    await navigate('/inventory/items/d1');

    const el = await renderTitle();
    expect(el.querySelector('a[href]')?.textContent?.trim()).toBe('Items');
    expect(el.querySelector('a[href]')?.getAttribute('href')).toBe('/inventory/items');
    expect(el.textContent).toContain('Cordless Drill'); // leaf crumb = durable name
  });

  it('renders the registered breadcrumb resolving the name from the consumable kind', async () => {
    itemApi.result = of(CONSUMABLE_ITEM);
    await navigate('/inventory/items/c1');

    const el = await renderTitle();
    expect(el.textContent).toContain('Wood Screws'); // leaf crumb resolved from the consumable payload
  });
});
