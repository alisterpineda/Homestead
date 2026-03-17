import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { describe, it, expect, beforeEach } from 'vitest';
import { CreateOcrEngineDialogComponent } from './create-ocr-engine-dialog.component';

describe('CreateOcrEngineDialogComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient()],
    });
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(CreateOcrEngineDialogComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should have empty name initially', () => {
    const fixture = TestBed.createComponent(CreateOcrEngineDialogComponent);
    expect(fixture.componentInstance.name()).toBe('');
  });

  it('should have ["Eng"] as the default languages', () => {
    const fixture = TestBed.createComponent(CreateOcrEngineDialogComponent);
    expect(fixture.componentInstance.selectedLanguages()).toEqual(['Eng']);
  });

  it('should not be creating initially', () => {
    const fixture = TestBed.createComponent(CreateOcrEngineDialogComponent);
    expect(fixture.componentInstance.creating()).toBe(false);
  });

  it('should have empty createError initially', () => {
    const fixture = TestBed.createComponent(CreateOcrEngineDialogComponent);
    expect(fixture.componentInstance.createError()).toBe('');
  });

  it('should not be submittable with empty name', () => {
    const fixture = TestBed.createComponent(CreateOcrEngineDialogComponent);
    expect(fixture.componentInstance.canSubmit()).toBe(false);
  });

  it('should be submittable with non-empty name', () => {
    const fixture = TestBed.createComponent(CreateOcrEngineDialogComponent);
    fixture.componentInstance.name.set('My OCR Engine');
    expect(fixture.componentInstance.canSubmit()).toBe(true);
  });

  it('should not be submittable with whitespace-only name', () => {
    const fixture = TestBed.createComponent(CreateOcrEngineDialogComponent);
    fixture.componentInstance.name.set('   ');
    expect(fixture.componentInstance.canSubmit()).toBe(false);
  });

  it('should not be submittable with empty languages', () => {
    const fixture = TestBed.createComponent(CreateOcrEngineDialogComponent);
    fixture.componentInstance.name.set('My Engine');
    fixture.componentInstance.selectedLanguages.set([]);
    expect(fixture.componentInstance.canSubmit()).toBe(false);
  });

  it('should reset state when dialog opens', () => {
    const fixture = TestBed.createComponent(CreateOcrEngineDialogComponent);
    const component = fixture.componentInstance;

    component.name.set('Some Engine');
    component.selectedLanguages.set(['Fra']);
    component.creating.set(true);
    component.createError.set('Something failed');

    component.onStateChanged('open');

    expect(component.name()).toBe('');
    expect(component.selectedLanguages()).toEqual(['Eng']);
    expect(component.creating()).toBe(false);
    expect(component.createError()).toBe('');
  });

  it('should not reset state when dialog closes', () => {
    const fixture = TestBed.createComponent(CreateOcrEngineDialogComponent);
    const component = fixture.componentInstance;

    component.name.set('Some Engine');
    component.onStateChanged('closed');
    expect(component.name()).toBe('Some Engine');
  });

  it('should extract input value from event via asInputValue', () => {
    const fixture = TestBed.createComponent(CreateOcrEngineDialogComponent);
    const event = { target: { value: 'test value' } } as unknown as Event;
    expect(fixture.componentInstance.asInputValue(event)).toBe('test value');
  });

  it('should have a languages list', () => {
    const fixture = TestBed.createComponent(CreateOcrEngineDialogComponent);
    expect(fixture.componentInstance.languages).toBeDefined();
    expect(fixture.componentInstance.languages.length).toBeGreaterThan(0);
  });
});
