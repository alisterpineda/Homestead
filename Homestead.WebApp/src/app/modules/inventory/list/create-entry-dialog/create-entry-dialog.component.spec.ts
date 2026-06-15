import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { CreateEntryDialogComponent } from './create-entry-dialog.component';
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
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
  });

  function create(initialLocation: ItemLocationValue | null = null):
    ComponentFixture<CreateEntryDialogComponent> {
    const fixture = TestBed.createComponent(CreateEntryDialogComponent);
    fixture.componentRef.setInput('initialLocation', initialLocation);
    fixture.detectChanges();
    return fixture;
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
});
