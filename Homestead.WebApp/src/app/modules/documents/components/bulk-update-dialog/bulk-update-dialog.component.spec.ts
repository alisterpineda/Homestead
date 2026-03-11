import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Component, signal } from '@angular/core';
import { BulkUpdateDialogComponent } from './bulk-update-dialog.component';

@Component({
  imports: [BulkUpdateDialogComponent],
  template: `<app-bulk-update-dialog [selectedIds]="ids()" />`,
})
class TestHostComponent {
  ids = signal<Set<string>>(new Set(['id1', 'id2']));
}

describe('BulkUpdateDialogComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let component: BulkUpdateDialogComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });
    fixture = TestBed.createComponent(TestHostComponent);
    fixture.detectChanges();
    component = fixture.debugElement.children[0].componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('canApply should be false when no tags or parties selected', () => {
    expect(component.canApply()).toBe(false);
  });

  it('canApply should be true when tags are selected', () => {
    component.tagPaths.set(['finance/invoices']);
    expect(component.canApply()).toBe(true);
  });

  it('canApply should be true when parties are selected', () => {
    component.parties.set([{ id: '1', name: 'Party A' }]);
    expect(component.canApply()).toBe(true);
  });

  it('should reset state on dialog open', () => {
    component.tagPaths.set(['some/tag']);
    component.parties.set([{ id: '1', name: 'Party A' }]);
    component.onStateChanged('open');
    expect(component.tagPaths()).toEqual([]);
    expect(component.parties()).toEqual([]);
    expect(component.applying()).toBe(false);
  });
});
