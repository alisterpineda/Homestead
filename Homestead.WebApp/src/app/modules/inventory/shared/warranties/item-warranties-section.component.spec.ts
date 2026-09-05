import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { By } from '@angular/platform-browser';
import { ItemWarrantiesSectionComponent } from './item-warranties-section.component';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import type { ItemWarrantyResponse } from '@/api/generated/models/item-warranty-response';
import {
  ItemWarrantyComposerComponent,
  type WarrantyPayload,
} from './item-warranty-composer.component';

function warranty(over: Partial<ItemWarrantyResponse> = {}): ItemWarrantyResponse {
  return {
    id: 'w1',
    itemId: 'item1',
    provider: null,
    expiresOn: '2028-03-12',
    notes: null,
    status: 'Active',
    createdAt: '2026-06-01T00:00:00Z',
    updatedAt: '2026-06-01T00:00:00Z',
    ...over,
  };
}

class StubItemApi {
  rows: ItemWarrantyResponse[] = [];
  listCalls: { id: string }[] = [];
  listFails = false;

  createCalls: { id: string; body: WarrantyPayload }[] = [];
  updateCalls: { id: string; warrantyId: string; body: WarrantyPayload }[] = [];
  deleteCalls: { id: string; warrantyId: string }[] = [];
  deleteFailure: unknown = null;

  getItemWarranties = (p: { id: string }) => {
    this.listCalls.push(p);
    return this.listFails ? throwError(() => new Error('boom')) : of(this.rows);
  };
  createItemWarranty = (p: { id: string; body: WarrantyPayload }) => {
    this.createCalls.push(p);
    return of(warranty({ id: 'new' }));
  };
  updateItemWarranty = (p: { id: string; warrantyId: string; body: WarrantyPayload }) => {
    this.updateCalls.push(p);
    return of(warranty({ id: p.warrantyId }));
  };
  deleteItemWarranty = (p: { id: string; warrantyId: string }) => {
    this.deleteCalls.push(p);
    return this.deleteFailure ? throwError(() => this.deleteFailure) : of(undefined);
  };
}

describe('ItemWarrantiesSectionComponent', () => {
  let api: StubItemApi;
  let fixture: ComponentFixture<ItemWarrantiesSectionComponent>;

  beforeEach(() => {
    api = new StubItemApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: InventoryItemApiService, useValue: api },
      ],
    });
  });

  async function create(brandName: string | null = null): Promise<ItemWarrantiesSectionComponent> {
    fixture = TestBed.createComponent(ItemWarrantiesSectionComponent);
    fixture.componentRef.setInput('itemId', 'item1');
    fixture.componentRef.setInput('brandName', brandName);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const host = () => fixture.nativeElement as HTMLElement;
  const text = () => host().textContent ?? '';
  const rows = () => Array.from(host().querySelectorAll<HTMLElement>('li'));
  const byText = (label: string) =>
    Array.from(host().querySelectorAll<HTMLButtonElement>('button')).find(b => b.textContent?.trim() === label);
  const internals = <T,>(c: ItemWarrantiesSectionComponent) => c as unknown as T;
  /** The nth hosted composer instance — the chip menu is an overlay, so it is driven through here. */
  const composerAt = (index: number) =>
    fixture.debugElement.queryAll(By.directive(ItemWarrantyComposerComponent))[index]
      .componentInstance as unknown as { chooseNoExpiry(): void };

  it('reads the list once and renders one row per warranty', async () => {
    api.rows = [warranty({ id: 'w1', provider: 'Home Depot' }), warranty({ id: 'w2', expiresOn: null })];
    await create();

    expect(api.listCalls).toEqual([{ id: 'item1' }]);
    expect(rows()).toHaveLength(2);
    expect(text()).toContain('Home Depot');
    expect(text()).toContain('until Mar 12, 2028');
    expect(text()).toContain('no expiry');
  });

  it('resolves a null provider against the item brand', async () => {
    api.rows = [warranty()];
    await create('Bosch');
    expect(text()).toContain('Bosch · manufacturer');

    api.rows = [warranty()];
    await create(null);
    expect(text()).toContain('Manufacturer');
  });

  it('renders the tier note inline and pills only the two states that want the eye', async () => {
    api.rows = [
      warranty({ id: 'w1', notes: 'motor only', status: 'Expiring' }),
      warranty({ id: 'w2', status: 'Expired', expiresOn: '2019-01-05' }),
      warranty({ id: 'w3', status: 'Active' }),
    ];
    await create();

    expect(text()).toContain('motor only');
    expect(rows()[0].textContent).toContain('Expiring soon');
    expect(rows()[1].textContent).toContain('Expired');
    expect(rows()[1].textContent).toContain('expired Jan 5, 2019');
    // An in-force warranty is the unremarkable case — no pill at all.
    expect(rows()[2].textContent).not.toContain('Active');
  });

  it('mutes an expired row’s provider label', async () => {
    api.rows = [warranty({ status: 'Expired', provider: 'Home Depot' })];
    await create();

    const label = Array.from(rows()[0].querySelectorAll('span'))
      .find(s => s.textContent?.trim() === 'Home Depot');
    expect(label?.className).toContain('text-muted-foreground');
  });

  it('renders the empty line when nothing is recorded', async () => {
    await create();
    expect(text()).toContain('No warranties recorded.');
  });

  it('renders the error state (not a false empty) and re-fetches on Retry', async () => {
    api.listFails = true;
    await create();

    expect(text()).toContain("Couldn't load warranties.");
    expect(text()).not.toContain('No warranties recorded.');

    api.listFails = false;
    api.rows = [warranty({ provider: 'Home Depot' })];
    const before = api.listCalls.length;
    byText('Retry')!.click();
    await settle();

    expect(api.listCalls.length).toBe(before + 1);
    expect(text()).toContain('Home Depot');
  });

  it('posts a new warranty, clears the composer (chip included), and reloads', async () => {
    await create();
    const composer = host().querySelector('app-item-warranty-composer')!;
    const chip = composer.querySelector<HTMLButtonElement>('button[aria-label^="Expiry:"]')!;
    const provider = composer.querySelector<HTMLInputElement>('input[hlmInputGroupInput]')!;

    provider.value = 'Home Depot';
    provider.dispatchEvent(new Event('input'));
    // Choosing an expiry is what unlocks Add; the chip menu lives in an overlay, so drive the
    // composer directly (its own spec covers the menu wiring).
    composerAt(0).chooseNoExpiry();
    await settle();

    const before = api.listCalls.length;
    composer.querySelector<HTMLButtonElement>('button[aria-label="Add warranty"]')!.click();
    await settle();

    expect(api.createCalls).toEqual([
      { id: 'item1', body: { provider: 'Home Depot', expiresOn: null, notes: null } },
    ]);
    expect(provider.value).toBe('');
    expect(chip.textContent).toContain('Expires');
    expect(api.listCalls.length).toBe(before + 1);
  });

  it('swaps the row for a composer, disables the header composer, saves, then reloads', async () => {
    api.rows = [warranty({ provider: 'Home Depot', notes: 'motor only' })];
    const component = await create();

    // An expiry already chosen in the header must not keep its Add button live during a row edit:
    // the `disabled` input has to gate submit, not just the fields.
    composerAt(0).chooseNoExpiry();
    await settle();
    internals<{ startEdit(r: ItemWarrantyResponse): void }>(component).startEdit(api.rows[0]);
    await settle();

    const composers = host().querySelectorAll('app-item-warranty-composer');
    expect(composers).toHaveLength(2);
    expect(composers[0].querySelector('input[hlmInputGroupInput]')!.hasAttribute('disabled')).toBe(true);
    expect(composers[0].querySelector<HTMLButtonElement>('button[aria-label="Add warranty"]')!.disabled).toBe(true);
    expect(composers[1].querySelector<HTMLInputElement>('input[hlmInputGroupInput]')!.value).toBe('Home Depot');

    const before = api.listCalls.length;
    Array.from(composers[1].querySelectorAll<HTMLButtonElement>('button'))
      .find(b => b.textContent?.trim() === 'Save')!
      .click();
    await settle();

    expect(api.updateCalls).toEqual([{
      id: 'item1',
      warrantyId: 'w1',
      body: { provider: 'Home Depot', expiresOn: '2028-03-12', notes: 'motor only' },
    }]);
    expect(api.listCalls.length).toBe(before + 1);
    expect(host().querySelectorAll('app-item-warranty-composer')).toHaveLength(1);
  });

  it('restores the row on Cancel without calling the API', async () => {
    api.rows = [warranty({ provider: 'Home Depot' })];
    const component = await create();
    internals<{ startEdit(r: ItemWarrantyResponse): void }>(component).startEdit(api.rows[0]);
    await settle();

    const editComposer = host().querySelectorAll('app-item-warranty-composer')[1];
    Array.from(editComposer.querySelectorAll<HTMLButtonElement>('button'))
      .find(b => b.textContent?.trim() === 'Cancel')!
      .click();
    await settle();

    expect(api.updateCalls).toEqual([]);
    expect(host().querySelectorAll('app-item-warranty-composer')).toHaveLength(1);
    expect(text()).toContain('Home Depot');
  });

  it('deletes on confirm and re-reads the list', async () => {
    api.rows = [warranty()];
    const component = await create();
    const before = api.listCalls.length;

    internals<{ onDeleteRequest(r: ItemWarrantyResponse): void }>(component).onDeleteRequest(api.rows[0]);
    let closed = false;
    internals<{ confirmDelete(ctx: { close: () => void }): void }>(component)
      .confirmDelete({ close: () => { closed = true; } });
    await settle();

    expect(api.deleteCalls).toEqual([{ id: 'item1', warrantyId: 'w1' }]);
    expect(closed).toBe(true);
    expect(api.listCalls.length).toBe(before + 1);
  });

  it('shows the rejection reason inline and keeps the delete dialog open on a 400', async () => {
    api.rows = [warranty()];
    api.deleteFailure = new HttpErrorResponse({ status: 400, error: { detail: 'Nope.' } });
    const component = await create();

    internals<{ onDeleteRequest(r: ItemWarrantyResponse): void }>(component).onDeleteRequest(api.rows[0]);
    let closed = false;
    internals<{ confirmDelete(ctx: { close: () => void }): void }>(component)
      .confirmDelete({ close: () => { closed = true; } });
    await settle();

    expect(closed).toBe(false);
    expect(internals<{ deleteError(): string }>(component).deleteError()).toBe('Nope.');
    expect(internals<{ deleteState(): string }>(component).deleteState()).toBe('open');
  });

  it('re-reads when the host bumps refreshTick', async () => {
    await create();
    const before = api.listCalls.length;

    fixture.componentRef.setInput('refreshTick', 1);
    await settle();

    expect(api.listCalls.length).toBe(before + 1);
  });
});
