import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { describe, it, expect, beforeEach } from 'vitest';
import { CreateTextExtractionEngineDialogComponent } from './create-text-extraction-engine-dialog.component';

describe('CreateTextExtractionEngineDialogComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient()],
    });
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should have empty name initially', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    expect(fixture.componentInstance.name()).toBe('');
  });

  it('should default to Tesseract engine type', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    expect(fixture.componentInstance.selectedEngineType()).toBe('Tesseract');
  });

  it('should have engine type options', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    const options = fixture.componentInstance.engineTypeOptions;
    expect(options.length).toBe(2);
    expect(options[0].value).toBe('Tesseract');
    expect(options[1].value).toBe('AzureDocumentIntelligence');
  });

  it('should have ["Eng"] as the default languages', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    expect(fixture.componentInstance.selectedLanguages()).toEqual(['Eng']);
  });

  it('should have empty endpoint and apiKey initially', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    expect(fixture.componentInstance.endpoint()).toBe('');
    expect(fixture.componentInstance.apiKey()).toBe('');
  });

  it('should not be creating initially', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    expect(fixture.componentInstance.creating()).toBe(false);
  });

  it('should have empty createError initially', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    expect(fixture.componentInstance.createError()).toBe('');
  });

  it('should not be submittable with empty name', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    expect(fixture.componentInstance.canSubmit()).toBe(false);
  });

  it('should be submittable with non-empty name for Tesseract', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    fixture.componentInstance.name.set('My OCR Engine');
    expect(fixture.componentInstance.canSubmit()).toBe(true);
  });

  it('should not be submittable with whitespace-only name', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    fixture.componentInstance.name.set('   ');
    expect(fixture.componentInstance.canSubmit()).toBe(false);
  });

  it('should not be submittable with empty languages for Tesseract', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    fixture.componentInstance.name.set('My Engine');
    fixture.componentInstance.selectedLanguages.set([]);
    expect(fixture.componentInstance.canSubmit()).toBe(false);
  });

  it('should be submittable for Azure DI with name, endpoint, and apiKey', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    fixture.componentInstance.selectedEngineType.set('AzureDocumentIntelligence');
    fixture.componentInstance.name.set('Azure Engine');
    fixture.componentInstance.endpoint.set('https://test.cognitiveservices.azure.com');
    fixture.componentInstance.apiKey.set('key123');
    expect(fixture.componentInstance.canSubmit()).toBe(true);
  });

  it('should not be submittable for Azure DI with missing endpoint', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    fixture.componentInstance.selectedEngineType.set('AzureDocumentIntelligence');
    fixture.componentInstance.name.set('Azure Engine');
    fixture.componentInstance.apiKey.set('key123');
    expect(fixture.componentInstance.canSubmit()).toBe(false);
  });

  it('should not be submittable for Azure DI with missing apiKey', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    fixture.componentInstance.selectedEngineType.set('AzureDocumentIntelligence');
    fixture.componentInstance.name.set('Azure Engine');
    fixture.componentInstance.endpoint.set('https://test.cognitiveservices.azure.com');
    expect(fixture.componentInstance.canSubmit()).toBe(false);
  });

  it('should not be submittable for Azure DI with whitespace-only endpoint', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    fixture.componentInstance.selectedEngineType.set('AzureDocumentIntelligence');
    fixture.componentInstance.name.set('Azure Engine');
    fixture.componentInstance.endpoint.set('   ');
    fixture.componentInstance.apiKey.set('key123');
    expect(fixture.componentInstance.canSubmit()).toBe(false);
  });

  it('should reset all state when dialog opens', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    const component = fixture.componentInstance;

    component.name.set('Some Engine');
    component.selectedEngineType.set('AzureDocumentIntelligence');
    component.selectedLanguages.set(['Fra']);
    component.endpoint.set('https://test.com');
    component.apiKey.set('secret');
    component.creating.set(true);
    component.createError.set('Something failed');

    component.onStateChanged('open');

    expect(component.name()).toBe('');
    expect(component.selectedEngineType()).toBe('Tesseract');
    expect(component.selectedLanguages()).toEqual(['Eng']);
    expect(component.endpoint()).toBe('');
    expect(component.apiKey()).toBe('');
    expect(component.creating()).toBe(false);
    expect(component.createError()).toBe('');
  });

  it('should not reset state when dialog closes', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    const component = fixture.componentInstance;

    component.name.set('Some Engine');
    component.onStateChanged('closed');
    expect(component.name()).toBe('Some Engine');
  });

  it('should extract input value from event via asInputValue', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    const event = { target: { value: 'test value' } } as unknown as Event;
    expect(fixture.componentInstance.asInputValue(event)).toBe('test value');
  });

  it('should have a languages list', () => {
    const fixture = TestBed.createComponent(CreateTextExtractionEngineDialogComponent);
    expect(fixture.componentInstance.languages).toBeDefined();
    expect(fixture.componentInstance.languages.length).toBeGreaterThan(0);
  });
});
