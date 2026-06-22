import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { describe, it, expect, beforeEach } from 'vitest';
import { BulkReprocessDialogComponent } from './bulk-reprocess-dialog.component';

describe('BulkReprocessDialogComponent', () => {
  let fixture: ComponentFixture<BulkReprocessDialogComponent>;
  let component: BulkReprocessDialogComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient()],
    });
    fixture = TestBed.createComponent(BulkReprocessDialogComponent);
    fixture.componentRef.setInput('documentIds', new Set(['doc-1', 'doc-2']));
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should not be reprocessing initially', () => {
    expect(component.reprocessing()).toBe(false);
  });

  it('should compute document count from input', () => {
    expect(component.documentCount()).toBe(2);
  });

  it('should reset reprocessing when dialog opens', () => {
    component.reprocessing.set(true);
    component.onStateChanged('open');
    expect(component.reprocessing()).toBe(false);
  });
});
