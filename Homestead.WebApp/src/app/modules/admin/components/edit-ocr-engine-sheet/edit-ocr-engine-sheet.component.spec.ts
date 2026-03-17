import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { EditOcrEngineSheetComponent } from './edit-ocr-engine-sheet.component';

describe('EditOcrEngineSheetComponent', () => {
  let fixture: ComponentFixture<EditOcrEngineSheetComponent>;
  let component: EditOcrEngineSheetComponent;

  beforeEach(() => {
    vi.stubGlobal('localStorage', {
      getItem: vi.fn().mockReturnValue(null),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });

    TestBed.configureTestingModule({
      providers: [provideHttpClient()],
    });

    fixture = TestBed.createComponent(EditOcrEngineSheetComponent);
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

  it('should have Eng as default editLanguage', () => {
    expect(component.editLanguage()).toBe('Eng');
  });

  it('should have null editConfiguration initially', () => {
    expect(component.editConfiguration()).toBeNull();
  });

  it('should not be saving initially', () => {
    expect(component.saving()).toBe(false);
  });

  it('should not be deleting initially', () => {
    expect(component.deleting()).toBe(false);
  });

  it('should not be confirming delete initially', () => {
    expect(component.confirmingDelete()).toBe(false);
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

  describe('getLanguageLabel', () => {
    it('should return English for null configuration', () => {
      expect(component.getLanguageLabel(null)).toBe('English');
    });

    it('should return English for undefined configuration', () => {
      expect(component.getLanguageLabel(undefined)).toBe('English');
    });

    it('should return the correct label for a known language', () => {
      const config = JSON.stringify({ language: 'Fra' });
      expect(component.getLanguageLabel(config)).toBe('French');
    });

    it('should return the raw language value for an unknown language', () => {
      const config = JSON.stringify({ language: 'Xyz' });
      expect(component.getLanguageLabel(config)).toBe('Xyz');
    });

    it('should return Unknown for invalid JSON', () => {
      expect(component.getLanguageLabel('not-json')).toBe('Unknown');
    });
  });

  describe('onLanguageChange', () => {
    it('should update editLanguage and editConfiguration', () => {
      component.onLanguageChange('Fra');
      expect(component.editLanguage()).toBe('Fra');
      expect(component.editConfiguration()).toBe(JSON.stringify({ language: 'Fra' }));
    });
  });

  describe('onStateChanged', () => {
    it('should reset confirmingDelete when closed', () => {
      component.confirmingDelete.set(true);
      component.onStateChanged('closed');
      expect(component.confirmingDelete()).toBe(false);
    });
  });
});
