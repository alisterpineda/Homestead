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

  /** The private BrnDialog view child (HlmDialog extends it), so tests can watch close(). */
  function dialogOf(component: CreateEntryDialogComponent): { close: () => void } | undefined {
    return (component as unknown as { dialog: () => { close: () => void } | undefined }).dialog();
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

  it('creates a regular durable without sending a type/container flag', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.name.set('Cordless drill');

    component.handleSubmit(new Event('submit'));

    expect(createDurable).toHaveBeenCalledTimes(1);
    const body = submittedDurableBody();
    expect(body.name).toBe('Cordless drill');
    // The dialog no longer carries a type selector — container-ness defaults false server-side.
    expect(body.isContainer).toBeUndefined();
    expect('durableKind' in body).toBe(false);
  });

  it('seeds starting stock at an area picked in the dialog, with no pre-filled location', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.onKindChange('consumable');
    component.name.set('Batteries');
    component.onLocationChange({ kind: 'area', id: 'garage' });
    component.initialQuantity.set('12');

    component.handleSubmit(new Event('submit'));

    expect(createConsumable).toHaveBeenCalledTimes(1);
    const body = submittedBody();
    expect(body.name).toBe('Batteries');
    expect(body.initialQuantity).toBe(12);
    expect(body.initialAreaId).toBe('garage');
    expect(body.initialContainerItemId).toBeNull();
  });

  it('seeds starting stock at a container picked in the dialog, with no pre-filled location', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.onKindChange('consumable');
    component.name.set('Screws');
    component.onLocationChange({ kind: 'container', id: 'chest' });
    component.initialQuantity.set('25');

    component.handleSubmit(new Event('submit'));

    const body = submittedBody();
    expect(body.initialQuantity).toBe(25);
    expect(body.initialContainerItemId).toBe('chest');
    expect(body.initialAreaId).toBeNull();
  });

  it('seeds starting stock at a drilled area carried in via initialLocation', () => {
    const fixture = create({ kind: 'area', id: 'garage' });
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.onKindChange('consumable');
    component.name.set('Batteries');
    component.initialQuantity.set('12');

    component.handleSubmit(new Event('submit'));

    const body = submittedBody();
    expect(body.initialQuantity).toBe(12);
    expect(body.initialAreaId).toBe('garage');
  });

  it('sends all three stock fields as null for an unplaced consumable with a blank quantity', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.onKindChange('consumable');
    component.name.set('Batteries');
    // Unplaced + no quantity — the bare "define the type, stock it later" create.

    expect(component.stockHint()).toBeNull();
    expect(component.canSubmit()).toBe(true);
    component.handleSubmit(new Event('submit'));

    const body = submittedBody();
    expect(body.initialQuantity).toBeNull();
    expect(body.initialAreaId).toBeNull();
    expect(body.initialContainerItemId).toBeNull();
    expect(successToast).toHaveBeenCalledWith('Consumable created.');
  });

  it('blocks a quantity with no location and a location with no quantity, symmetrically', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.onKindChange('consumable');
    component.name.set('Batteries');

    // Quantity, nowhere to put it.
    component.initialQuantity.set('5');
    expect(component.stockHint()).toBe('Pick a location to hold this stock, or leave the quantity blank.');
    expect(component.canSubmit()).toBe(false);

    // A place, but no quantity to put in it.
    component.initialQuantity.set('');
    component.onLocationChange({ kind: 'area', id: 'garage' });
    expect(component.stockHint()).toBe('Enter a starting quantity, or set the location to Unplaced.');
    expect(component.canSubmit()).toBe(false);

    // A 0 reads as "none yet", so it's blocked the same way a blank is.
    component.initialQuantity.set('0');
    expect(component.stockHint()).toBe('Enter a starting quantity, or set the location to Unplaced.');
    expect(component.canSubmit()).toBe(false);

    // Both halves present — consistent.
    component.initialQuantity.set('5');
    expect(component.stockHint()).toBeNull();
    expect(component.canSubmit()).toBe(true);
  });

  it('names an invalid quantity or threshold instead of disabling submit silently', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.onKindChange('consumable');
    component.name.set('Batteries');
    component.onLocationChange({ kind: 'area', id: 'garage' });

    // min="0" is a constraint, not a sanitiser — a typed "-5" still reaches the signal.
    for (const bad of ['-5', 'abc']) {
      component.initialQuantity.set(bad);
      expect(component.stockHint()).toBe('Enter a starting quantity of 0 or more.');
      expect(component.canSubmit()).toBe(false);
    }

    component.initialQuantity.set('5');
    expect(component.stockHint()).toBeNull();
    expect(component.canSubmit()).toBe(true);

    // The threshold lives under the collapsed "More details", so its hint must surface regardless.
    component.threshold.set('-1');
    expect(component.thresholdHint()).toBe('Restock threshold must be 0 or more.');
    expect(component.canSubmit()).toBe(false);

    component.threshold.set('2');
    expect(component.thresholdHint()).toBeNull();
    expect(component.canSubmit()).toBe(true);
  });

  it('never blocks a durable on the stock hint', () => {
    const fixture = create({ kind: 'area', id: 'garage' });
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.name.set('Cordless drill');

    expect(component.kind()).toBe('durable');
    expect(component.stockHint()).toBeNull();
    expect(component.canSubmit()).toBe(true);
  });

  it('names the seeded quantity and unit in the success toast', () => {
    const fixture = create({ kind: 'area', id: 'garage' }, 'Pantry');
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.onKindChange('consumable');
    component.name.set('Paper towels');
    component.unit.set('rolls');
    component.initialQuantity.set('12');

    component.handleSubmit(new Event('submit'));

    expect(successToast).toHaveBeenCalledWith('Consumable created with 12 rolls.');
  });

  it('falls back to the default unit word in the success toast', () => {
    const fixture = create({ kind: 'area', id: 'garage' });
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.onKindChange('consumable');
    component.name.set('Batteries');
    component.initialQuantity.set('12');

    component.handleSubmit(new Event('submit'));

    expect(successToast).toHaveBeenCalledWith('Consumable created with 12 units.');
  });

  it('keeps the dialog open and retains kind + location for "Create & add another"', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    const close = vi.spyOn(dialogOf(component)!, 'close');
    const created = vi.fn();
    component.created.subscribe(created);

    component.onStateChanged('open');
    component.onKindChange('consumable');
    component.moreOpen.set(true);
    component.name.set('Batteries');
    component.unit.set('packs');
    component.notes.set('spare');
    component.brand.set({ id: 'duracell', name: 'Duracell' });
    component.tags.set(['household']);
    component.threshold.set('4');
    component.onLocationChange({ kind: 'area', id: 'garage' });
    component.initialQuantity.set('6');

    component.submit(true);

    expect(createConsumable).toHaveBeenCalledTimes(1);
    expect(close).not.toHaveBeenCalled();
    // The host is not told yet: its refetch could flip a data-gated @if that mounts this dialog and
    // force-close it mid-batch. It hears once, when the dialog closes.
    expect(created).not.toHaveBeenCalled();
    // Kept: kind, location, and the More-details disclosure.
    expect(component.kind()).toBe('consumable');
    expect(component.areaId()).toBe('garage');
    expect(component.moreOpen()).toBe(true);
    // Cleared: everything item-specific.
    expect(component.name()).toBe('');
    expect(component.unit()).toBe('');
    expect(component.notes()).toBe('');
    expect(component.brand()).toBeNull();
    expect(component.tags()).toEqual([]);
    expect(component.threshold()).toBe('');
    expect(component.initialQuantity()).toBe('');
    expect(component.model()).toBe('');

    component.onStateChanged('closed');
    expect(created).toHaveBeenCalledTimes(1);
    // Nothing pending after that, so a reopen + close stays quiet.
    component.onStateChanged('open');
    component.onStateChanged('closed');
    expect(created).toHaveBeenCalledTimes(1);
  });

  it('closes the dialog on a plain Create', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    const close = vi.spyOn(dialogOf(component)!, 'close');
    component.onStateChanged('open');
    component.name.set('Cordless drill');

    component.handleSubmit(new Event('submit'));

    expect(close).toHaveBeenCalledTimes(1);
  });

  it('sends the selected brandId and trimmed model when creating a durable', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.name.set('MacBook Pro');
    component.brand.set({ id: 'apple-1', name: 'Apple' });
    component.model.set('  MRW23  ');

    component.handleSubmit(new Event('submit'));

    const body = submittedDurableBody();
    expect(body.brandId).toBe('apple-1');
    expect(body.model).toBe('MRW23');
  });

  it('sends null brandId/model for a durable with no brand or model', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.name.set('Cordless drill');

    component.handleSubmit(new Event('submit'));

    const body = submittedDurableBody();
    expect(body.brandId).toBeNull();
    expect(body.model).toBeNull();
  });

  it('sends the selected brandId when creating a consumable (no model field)', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.onStateChanged('open');
    component.onKindChange('consumable');
    component.name.set('Lightning cables');
    component.brand.set({ id: 'apple-1', name: 'Apple' });

    component.handleSubmit(new Event('submit'));

    const body = submittedBody();
    expect(body.brandId).toBe('apple-1');
    expect('model' in body).toBe(false);
  });

  it('resets the kind, brand, model and More-details disclosure on open', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.kind.set('consumable');
    component.brand.set({ id: 'stale', name: 'Stale' });
    component.model.set('stale-model');
    component.moreOpen.set(true);

    component.onStateChanged('open');

    expect(component.kind()).toBe('durable');
    expect(component.brand()).toBeNull();
    expect(component.model()).toBe('');
    expect(component.moreOpen()).toBe(false);
  });
});
