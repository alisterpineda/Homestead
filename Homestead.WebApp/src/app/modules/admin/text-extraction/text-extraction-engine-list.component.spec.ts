import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { TextExtractionEngineListComponent } from './text-extraction-engine-list.component';
import type { TextExtractionEngineResponse } from '@/api/generated/models/text-extraction-engine-response';

describe('TextExtractionEngineListComponent', () => {
  let fixture: ComponentFixture<TextExtractionEngineListComponent>;
  let component: TextExtractionEngineListComponent;

  beforeEach(() => {
    vi.stubGlobal('localStorage', {
      getItem: vi.fn().mockReturnValue(null),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });

    fixture = TestBed.createComponent(TextExtractionEngineListComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('should create the component', () => {
    expect(component).toBeTruthy();
  });

  it('should initialise with loading false and no error', () => {
    expect(component.loading()).toBe(false);
    expect(component.error()).toBe(false);
  });

  it('should initialise with no selected engine', () => {
    expect(component.selectedEngine()).toBeNull();
    expect(component.sheetState()).toBe('closed');
  });

  it('should have empty search input initially', () => {
    expect(component.searchInput()).toBe('');
  });

  it('should have null data initially', () => {
    expect(component.data()).toBeNull();
  });

  it('should have refreshTrigger at 0 initially', () => {
    expect(component.refreshTrigger()).toBe(0);
  });

  it('should increment refreshTrigger on onRefresh', () => {
    expect(component.refreshTrigger()).toBe(0);
    component.onRefresh();
    expect(component.refreshTrigger()).toBe(1);
    component.onRefresh();
    expect(component.refreshTrigger()).toBe(2);
  });

  it('should open the sheet when an engine is clicked', () => {
    const engine: TextExtractionEngineResponse = {
      id: 'abc-123',
      name: 'Test Engine',
      engineType: 'Tesseract',
      isBuiltIn: false,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      configuration: null,
    };

    component.onEngineClick(engine);

    expect(component.selectedEngine()).toBe(engine);
    expect(component.sheetState()).toBe('open');
  });

  it('should close the sheet and clear selected engine on onSheetClosed', () => {
    const engine: TextExtractionEngineResponse = {
      id: 'abc-123',
      name: 'Test Engine',
      engineType: 'Tesseract',
      isBuiltIn: false,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      configuration: null,
    };

    component.onEngineClick(engine);
    expect(component.sheetState()).toBe('open');

    component.onSheetClosed();
    expect(component.sheetState()).toBe('closed');
    expect(component.selectedEngine()).toBeNull();
  });

  it('should close sheet and refresh on onEngineSaved', () => {
    const engine: TextExtractionEngineResponse = {
      id: 'abc-123',
      name: 'Test Engine',
      engineType: 'Tesseract',
      isBuiltIn: false,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      configuration: null,
    };

    component.onEngineClick(engine);
    const prevTrigger = component.refreshTrigger();

    component.onEngineSaved();
    expect(component.sheetState()).toBe('closed');
    expect(component.selectedEngine()).toBeNull();
    expect(component.refreshTrigger()).toBe(prevTrigger + 1);
  });

  it('should initialise with empty selection', () => {
    expect(component.selectedIds().size).toBe(0);
    expect(component.selectionCount()).toBe(0);
    expect(component.hasSelection()).toBe(false);
  });

  it('should toggle selection for an engine', () => {
    component.toggleSelection('id-1', true);
    expect(component.selectedIds().has('id-1')).toBe(true);
    expect(component.selectionCount()).toBe(1);

    component.toggleSelection('id-1', false);
    expect(component.selectedIds().has('id-1')).toBe(false);
    expect(component.selectionCount()).toBe(0);
  });

  it('should clear all selections', () => {
    component.toggleSelection('id-1', true);
    component.toggleSelection('id-2', true);
    expect(component.selectionCount()).toBe(2);

    component.clearSelection();
    expect(component.selectionCount()).toBe(0);
  });

  it('should report hasSelection correctly', () => {
    expect(component.hasSelection()).toBe(false);
    component.toggleSelection('id-1', true);
    expect(component.hasSelection()).toBe(true);
  });

  it('should initialise deleting as false', () => {
    expect(component.deleting()).toBe(false);
  });

  it('should extract input value from event via asInputValue', () => {
    const event = { target: { value: 'hello' } } as unknown as Event;
    expect(component.asInputValue(event)).toBe('hello');
  });

  it('should have empty searchTerm initially', () => {
    expect(component.searchTerm()).toBe('');
  });

  describe('getEngineTypeLabel', () => {
    it('should return Tesseract for Tesseract', () => {
      expect(component.getEngineTypeLabel('Tesseract')).toBe('Tesseract');
    });

    it('should return friendly label for AzureDocumentIntelligence', () => {
      expect(component.getEngineTypeLabel('AzureDocumentIntelligence')).toBe('Azure Document Intelligence');
    });

    it('should return raw value for unknown type', () => {
      expect(component.getEngineTypeLabel('SomeOtherType')).toBe('SomeOtherType');
    });
  });
});
