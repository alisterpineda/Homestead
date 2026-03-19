import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { EditTextExtractionEngineSheetComponent } from './edit-text-extraction-engine-sheet.component';

describe('EditTextExtractionEngineSheetComponent', () => {
  let fixture: ComponentFixture<EditTextExtractionEngineSheetComponent>;
  let component: EditTextExtractionEngineSheetComponent;

  beforeEach(() => {
    vi.stubGlobal('localStorage', {
      getItem: vi.fn().mockReturnValue(null),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });

    TestBed.configureTestingModule({
      providers: [provideHttpClient()],
    });

    fixture = TestBed.createComponent(EditTextExtractionEngineSheetComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('should create the component', () => {
    expect(component).toBeTruthy();
  });

  it('should have closed state by default', () => {
    expect(component.state()).toBe('closed');
  });

  it('should have null engine by default', () => {
    expect(component.engine()).toBeNull();
  });

  it('should have empty editName initially', () => {
    expect(component.editName()).toBe('');
  });

  it('should have ["Eng"] as default editLanguages', () => {
    expect(component.editLanguages()).toEqual(['Eng']);
  });

  it('should have null editConfiguration initially', () => {
    expect(component.editConfiguration()).toBeNull();
  });

  it('should not be saving initially', () => {
    expect(component.saving()).toBe(false);
  });

  it('should have no changes when engine is null', () => {
    expect(component.hasChanges()).toBe(false);
  });

  it('should have a languages list', () => {
    expect(component.languages).toBeDefined();
    expect(component.languages.length).toBeGreaterThan(0);
  });

  it('should extract input value from event via asInputValue', () => {
    const event = { target: { value: 'new name' } } as unknown as Event;
    expect(component.asInputValue(event)).toBe('new name');
  });

  describe('getLanguagesLabel', () => {
    it('should return English for null configuration', () => {
      expect(component.getLanguagesLabel(null)).toBe('English');
    });

    it('should return English for undefined configuration', () => {
      expect(component.getLanguagesLabel(undefined)).toBe('English');
    });

    it('should return the correct label for a single known language', () => {
      const config = JSON.stringify({ languages: ['Deu'] });
      expect(component.getLanguagesLabel(config)).toBe('German');
    });

    it('should return comma-separated labels for multiple languages', () => {
      const config = JSON.stringify({ languages: ['Eng', 'Fra'] });
      expect(component.getLanguagesLabel(config)).toBe('English, French');
    });

    it('should return the raw language value for an unknown language', () => {
      const config = JSON.stringify({ languages: ['Xyz'] });
      expect(component.getLanguagesLabel(config)).toBe('Xyz');
    });

    it('should return Unknown for invalid JSON', () => {
      expect(component.getLanguagesLabel('not-json')).toBe('Unknown');
    });

    it('should return English for empty languages array', () => {
      const config = JSON.stringify({ languages: [] });
      expect(component.getLanguagesLabel(config)).toBe('English');
    });
  });

  describe('onLanguagesChange', () => {
    it('should update editLanguages and editConfiguration', () => {
      component.onLanguagesChange(['Fra']);
      expect(component.editLanguages()).toEqual(['Fra']);
      expect(component.editConfiguration()).toBe(JSON.stringify({ languages: ['Fra'] }));
    });

    it('should handle multiple languages', () => {
      component.onLanguagesChange(['Eng', 'Fra']);
      expect(component.editLanguages()).toEqual(['Eng', 'Fra']);
      expect(component.editConfiguration()).toBe(JSON.stringify({ languages: ['Eng', 'Fra'] }));
    });
  });

  describe('onStateChanged', () => {
    it('should emit closed on state change to closed', () => {
      const spy = vi.fn();
      component.closed.subscribe(spy);
      component.onStateChanged('closed');
      expect(spy).toHaveBeenCalled();
    });
  });
});
