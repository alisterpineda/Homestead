import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { describe, it, expect, beforeEach } from 'vitest';
import { ReprocessDialogComponent } from './reprocess-dialog.component';

describe('ReprocessDialogComponent', () => {
  let fixture: ComponentFixture<ReprocessDialogComponent>;
  let component: ReprocessDialogComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient()],
    });
    fixture = TestBed.createComponent(ReprocessDialogComponent);
    fixture.componentRef.setInput('documentId', 'doc-1');
    fixture.componentRef.setInput('versionId', 'ver-1');
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should not be reprocessing initially', () => {
    expect(component.reprocessing()).toBe(false);
  });

  it('should reset reprocessing when dialog opens', () => {
    component.reprocessing.set(true);
    component.onStateChanged('open');
    expect(component.reprocessing()).toBe(false);
  });

  it('should not reset state when dialog closes', () => {
    component.reprocessing.set(true);
    component.onStateChanged('closed');
    expect(component.reprocessing()).toBe(true);
  });
});
