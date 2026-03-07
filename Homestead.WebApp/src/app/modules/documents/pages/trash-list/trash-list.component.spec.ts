import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { TrashListComponent } from './trash-list.component';

describe('TrashListComponent', () => {
  let fixture: ComponentFixture<TrashListComponent>;
  let component: TrashListComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });
    fixture = TestBed.createComponent(TrashListComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should initialise with loading false and no error', () => {
    expect(component.loading()).toBe(false);
    expect(component.error()).toBe(false);
  });

  it('should have no selection initially', () => {
    expect(component.isInSelectionState()).toBe(false);
    expect(component.selectionCount()).toBe(0);
  });

  describe('isSelected', () => {
    it('should return false when id is not in selectedDocumentIds', () => {
      expect(component.isSelected('abc')).toBe(false);
    });

    it('should return true when id is in selectedDocumentIds', () => {
      component.selectedDocumentIds.set(new Set(['abc']));
      expect(component.isSelected('abc')).toBe(true);
    });
  });

  describe('someSelected', () => {
    it('should be false when no data', () => {
      expect(component.someSelected()).toBe(false);
    });

    it('should be false when no items selected', () => {
      (component as any).data = signal({ items: [{ id: '1' }, { id: '2' }] });
      expect(component.someSelected()).toBe(false);
    });

    it('should be true when some but not all items selected', () => {
      (component as any).data = signal({ items: [{ id: '1' }, { id: '2' }] });
      component.selectedDocumentIds.set(new Set(['1']));
      expect(component.someSelected()).toBe(true);
    });

    it('should be false when all items selected', () => {
      (component as any).data = signal({ items: [{ id: '1' }, { id: '2' }] });
      component.selectedDocumentIds.set(new Set(['1', '2']));
      expect(component.someSelected()).toBe(false);
    });
  });

  describe('toggleSelectAll', () => {
    it('should select all items when none are selected', () => {
      (component as any).data = signal({ items: [{ id: '1' }, { id: '2' }] });
      component.toggleSelectAll();
      expect(component.selectedDocumentIds()).toEqual(new Set(['1', '2']));
    });

    it('should deselect all items when all are selected', () => {
      (component as any).data = signal({ items: [{ id: '1' }, { id: '2' }] });
      component.selectedDocumentIds.set(new Set(['1', '2']));
      component.toggleSelectAll();
      expect(component.selectedDocumentIds().size).toBe(0);
    });
  });

  describe('allPageSelected', () => {
    it('should be false when no data', () => {
      expect(component.allPageSelected()).toBe(false);
    });

    it('should be false when no items are selected', () => {
      (component as any).data = signal({ items: [{ id: '1' }] });
      expect(component.allPageSelected()).toBe(false);
    });

    it('should be true when all items are selected', () => {
      (component as any).data = signal({ items: [{ id: '1' }, { id: '2' }] });
      component.selectedDocumentIds.set(new Set(['1', '2']));
      expect(component.allPageSelected()).toBe(true);
    });
  });
});
