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

  it('should have null selectedEngineId initially', () => {
    expect(component.selectedEngineId()).toBeNull();
  });

  it('should have empty engines initially', () => {
    expect(component.engines()).toEqual([]);
  });

  it('should not be reprocessing initially', () => {
    expect(component.reprocessing()).toBe(false);
  });

  it('should not have load error initially', () => {
    expect(component.loadError()).toBe(false);
  });

  it('should not be loading engines initially', () => {
    expect(component.loadingEngines()).toBe(false);
  });

  it('should map empty string to null in onEngineChange', () => {
    component.onEngineChange('');
    expect(component.selectedEngineId()).toBeNull();
  });

  it('should store engine ID in onEngineChange', () => {
    component.onEngineChange('engine-123');
    expect(component.selectedEngineId()).toBe('engine-123');
  });

  it('should reset state when dialog opens', () => {
    component.selectedEngineId.set('some-engine');
    component.reprocessing.set(true);
    component.loadError.set(true);

    component.onStateChanged('open');

    expect(component.selectedEngineId()).toBeNull();
    expect(component.reprocessing()).toBe(false);
    expect(component.loadError()).toBe(false);
  });

  it('should not reset state when dialog closes', () => {
    component.selectedEngineId.set('some-engine');
    component.onStateChanged('closed');
    expect(component.selectedEngineId()).toBe('some-engine');
  });

  it('should set loadingEngines when dialog opens', () => {
    component.onStateChanged('open');
    expect(component.loadingEngines()).toBe(true);
  });
});
