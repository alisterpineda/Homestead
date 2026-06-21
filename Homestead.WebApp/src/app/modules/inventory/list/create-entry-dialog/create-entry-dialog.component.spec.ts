import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { toast } from '@spartan-ng/brain/sonner';
import { CreateEntryDialogComponent } from './create-entry-dialog.component';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import type { CreateConsumableCommand } from '@/api/generated/models/create-consumable-command';
import type { CreateDurableCommand } from '@/api/generated/models/create-durable-command';
import type { ItemLocationValue } from '../../shared/location-picker/location-picker.component';

// The embedded location picker's popover uses ResizeObserver.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

describe('CreateEntryDialogComponent', () => {
  let createConsumable: ReturnType<typeof vi.fn>;
  let createDurable: ReturnType<typeof vi.fn>;
  let successToast: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    createConsumable = vi.fn().mockReturnValue(of({ id: 'new-consumable' }));
    createDurable = vi.fn().mockReturnValue(of({ id: 'new-durable' }));
    successToast = vi.spyOn(toast, 'success').mockImplementation(() => undefined as never);
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: InventoryConsumableApiService, useValue: { createConsumable } },
        { provide: InventoryDurableApiService, useValue: { createDurable } },
      ],
    });
  });

  /** The last body passed to the mocked createDurable. */
  function submittedDurableBody(): CreateDurableCommand {
    return createDurable.mock.calls.at(-1)![0].body as CreateDurableCommand;
  }

  function create(initialLocation: ItemLocationValue | null = null, initialLocationLabel: string | null = null):
    ComponentFixture<CreateEntryDialogComponent> {
    const fixture = TestBed.createComponent(CreateEntryDialogComponent);
    fixture.componentRef.setInput('initialLocation', initialLocation);
    fixture.componentRef.setInput('initialLocationLabel', initialLocationLabel);
    fixture.detectChanges();
    return fixture;
  }

  /** The last body passed to the mocked createConsumable. */
  function submittedBody(): CreateConsumableCommand {
    return createConsumable.mock.calls.at(-1)![0].body as CreateConsumableCommand;
  }

  it('seeds the location from initialLocation on open', () => {
    const fixture = create({ kind: 'area', id: 'garage' });
    const component = fixture.componentInstance;

    component.onStateChanged('open');

    expect(component.areaId()).toBe('garage');
    expect(component.parentItemId()).toBeNull();
    expect(component.locationValue()).toEqual({ kind: 'area', id: 'garage' });
  });

  it('seeds a container initialLocation into parentItemId', () => {
    const fixture = create({ kind: 'container', id: 'chest' });
    const component = fixture.componentInstance;

    component.onStateChanged('open');

    expect(component.parentItemId()).toBe('chest');
    expect(component.areaId()).toBeNull();
  });

  it('defaults to unplaced when no initialLocation is given', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    // Simulate a dirty prior session to prove open resets it.
    component.areaId.set('stale');

    component.onStateChanged('open');

    expect(component.areaId()).toBeNull();
    expect(component.parentItemId()).toBeNull();
    expect(component.locationValue()).toEqual({ kind: 'unplaced' });
  });

  it('defaults to the durable kind and flips to consumable via the segmented control', () => {
    const fixture = create();
    const component = fixture.componentInstance;

    expect(component.kind()).toBe('durable');

    component.onKindChange('consumable');
    expect(component.kind()).toBe('consumable');

    component.onKindChange('durable');
    expect(component.kind()).toBe('durable');

    // A stray value from the toggle-group's wide type must be ignored, not crash or clear the kind.
    component.onKindChange(undefined);
    expect(component.kind()).toBe('durable');
  });

  it('exposes the starting-stock field gate only for a real pre-filled place', () => {
    // The quantity field is rendered via @if (placedInitialLocation()) — area/container yes,
    // unplaced/null no. The dialog content lives in an overlay portal that only mounts on open, so
    // we assert the gating computed rather than querying the not-yet-rendered DOM.
    expect(create({ kind: 'area', id: 'garage' }).componentInstance.placedInitialLocation())
      .toEqual({ kind: 'area', id: 'garage' });
    expect(create({ kind: 'container', id: 'chest' }).componentInstance.placedInitialLocation())
      .toEqual({ kind: 'container', id: 'chest' });
    expect(create({ kind: 'unplaced' }).componentInstance.placedInitialLocation()).toBeNull();
    expect(create(null).componentInstance.placedInitialLocation()).toBeNull();
  });

  it('sends durableKind=Plain by default when creating a durable', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.name.set('Cordless drill');

    component.handleSubmit(new Event('submit'));

    expect(createDurable).toHaveBeenCalledTimes(1);
    expect(submittedDurableBody().durableKind).toBe('Plain');
  });

  it('sends the chosen durableKind (Assembly) when creating a durable', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.name.set('Gaming PC');
    component.durableKind.set('Assembly');

    component.handleSubmit(new Event('submit'));

    const body = submittedDurableBody();
    expect(body.name).toBe('Gaming PC');
    expect(body.durableKind).toBe('Assembly');
  });

  it('resets the durable type to Plain on open', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.durableKind.set('Container');

    component.onStateChanged('open');

    expect(component.durableKind()).toBe('Plain');
  });

  it('seeds starting stock when a quantity is entered at a drilled area', () => {
    const fixture = create({ kind: 'area', id: 'garage' });
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.onKindChange('consumable');
    component.name.set('Batteries');
    component.initialQuantity.set('12');

    component.handleSubmit(new Event('submit'));

    expect(createConsumable).toHaveBeenCalledTimes(1);
    const body = submittedBody();
    expect(body.name).toBe('Batteries');
    expect(body.initialQuantity).toBe(12);
    expect(body.initialAreaId).toBe('garage');
    expect(body.initialContainerItemId).toBeNull();
  });

  it('seeds starting stock at a drilled container', () => {
    const fixture = create({ kind: 'container', id: 'chest' });
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.onKindChange('consumable');
    component.name.set('Screws');
    component.initialQuantity.set('25');

    component.handleSubmit(new Event('submit'));

    const body = submittedBody();
    expect(body.initialQuantity).toBe(25);
    expect(body.initialContainerItemId).toBe('chest');
    expect(body.initialAreaId).toBeNull();
  });

  it('omits stock fields when the quantity is left blank', () => {
    const fixture = create({ kind: 'area', id: 'garage' });
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.onKindChange('consumable');
    component.name.set('Batteries');
    // No starting quantity — a deliberate "stock it later" bare create.

    component.handleSubmit(new Event('submit'));

    const body = submittedBody();
    expect(body.initialQuantity).toBeNull();
    expect(body.initialAreaId).toBeNull();
    expect(body.initialContainerItemId).toBeNull();
  });

  it('omits stock fields for an unplaced consumable even if a quantity is somehow set', () => {
    const fixture = create(null);
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.onKindChange('consumable');
    component.name.set('Batteries');
    component.initialQuantity.set('5');

    component.handleSubmit(new Event('submit'));

    const body = submittedBody();
    expect(body.initialQuantity).toBeNull();
    expect(body.initialAreaId).toBeNull();
    expect(body.initialContainerItemId).toBeNull();
  });

  it('creates a bare consumable when a placed quantity is 0 (button enabled, no stock seeded)', () => {
    const fixture = create({ kind: 'area', id: 'garage' });
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.onKindChange('consumable');
    component.name.set('Batteries');
    component.initialQuantity.set('0');

    // A 0 quantity keeps the submit button enabled but must seed no stock — the "stock it later" path.
    expect(component.canSubmit()).toBe(true);
    component.handleSubmit(new Event('submit'));

    const body = submittedBody();
    expect(body.initialQuantity).toBeNull();
    expect(body.initialAreaId).toBeNull();
    expect(body.initialContainerItemId).toBeNull();
    expect(successToast).toHaveBeenCalledWith('Consumable created.');
  });

  it('names the seeded quantity and place in the success toast', () => {
    const fixture = create({ kind: 'area', id: 'garage' }, 'Pantry');
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.onKindChange('consumable');
    component.name.set('Batteries');
    component.initialQuantity.set('12');

    component.handleSubmit(new Event('submit'));

    expect(successToast).toHaveBeenCalledWith('Consumable created with 12 at Pantry.');
  });
});
