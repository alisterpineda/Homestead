import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { DocumentSettingsComponent } from './document-settings.component';

describe('DocumentSettingsComponent', () => {
  let fixture: ComponentFixture<DocumentSettingsComponent>;
  let component: DocumentSettingsComponent;

  beforeEach(() => {
    vi.stubGlobal('localStorage', {
      getItem: vi.fn().mockReturnValue(null),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });

    fixture = TestBed.createComponent(DocumentSettingsComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('should create the component', () => {
    expect(component).toBeTruthy();
  });

  it('should initialise with loading true', () => {
    expect(component.loading()).toBe(true);
  });

  it('should initialise with saving false', () => {
    expect(component.saving()).toBe(false);
  });

  it('should initialise with no load error', () => {
    expect(component.loadError()).toBe(false);
  });

  it('should initialise with empty engines', () => {
    expect(component.engines()).toEqual([]);
  });

  it('should initialise with null selectedEngineId', () => {
    expect(component.selectedEngineId()).toBeNull();
  });

  it('should have no changes initially', () => {
    expect(component.hasChanges()).toBe(false);
  });

  it('should not report stale engine when no saved engine', () => {
    expect(component.staleEngineId()).toBe(false);
  });

  it('should set selectedEngineId to null for empty string', () => {
    component.onEngineChange('');
    expect(component.selectedEngineId()).toBeNull();
  });

  it('should set selectedEngineId for non-empty string', () => {
    component.onEngineChange('some-id');
    expect(component.selectedEngineId()).toBe('some-id');
  });
});
