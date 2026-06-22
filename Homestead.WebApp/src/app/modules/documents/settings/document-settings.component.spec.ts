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

  it('should have no changes initially', () => {
    expect(component.hasChanges()).toBe(false);
  });

  it('should set selectedAutoClassify and report a change when toggled', () => {
    component.onAutoClassifyChange(true);
    expect(component.selectedAutoClassify()).toBe(true);
    expect(component.hasChanges()).toBe(true);
  });
});
