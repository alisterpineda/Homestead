import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { PriceSourceConfigFormComponent } from './price-source-config-form.component';
import type { PriceProviderConfigSchema } from '@/api/generated/models/price-provider-config-schema';

describe('PriceSourceConfigFormComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({}));

  function createComponentWith(schema: PriceProviderConfigSchema, initialJson: string | null) {
    const fixture = TestBed.createComponent(PriceSourceConfigFormComponent);
    fixture.componentRef.setInput('schema', schema);
    fixture.componentRef.setInput('initialJson', initialJson);
    fixture.detectChanges();
    return fixture;
  }

  it('omits unchanged Secret values from emitted payload', () => {
    const schema: PriceProviderConfigSchema = {
      fields: [
        { key: 'ApiKey', label: 'API key', type: 'Secret', isRequired: true },
        { key: 'BaseUrl', label: 'Base', type: 'String', isRequired: false },
      ],
    };
    const fixture = createComponentWith(schema, JSON.stringify({ ApiKey: '••••1234', BaseUrl: 'https://x' }));
    const captured = signal<string | null>(null);
    fixture.componentInstance.saved.subscribe(v => captured.set(v));

    fixture.componentInstance.emitSave();

    const payload = JSON.parse(captured()!);
    expect(payload.ApiKey).toBeUndefined();
    expect(payload.BaseUrl).toBe('https://x');
  });

  it('includes Secret value when user changes it', () => {
    const schema: PriceProviderConfigSchema = {
      fields: [{ key: 'ApiKey', label: 'API key', type: 'Secret', isRequired: true }],
    };
    const fixture = createComponentWith(schema, JSON.stringify({ ApiKey: '••••1234' }));
    fixture.componentInstance.setValue('ApiKey', 'rotated-fresh-key');

    const captured = signal<string | null>(null);
    fixture.componentInstance.saved.subscribe(v => captured.set(v));

    fixture.componentInstance.emitSave();

    const payload = JSON.parse(captured()!);
    expect(payload.ApiKey).toBe('rotated-fresh-key');
  });

  it('uses password input type for Secret fields', () => {
    const fixture = createComponentWith(
      { fields: [{ key: 'ApiKey', label: 'API key', type: 'Secret', isRequired: true }] },
      null);
    expect(fixture.componentInstance.inputType('Secret')).toBe('password');
    expect(fixture.componentInstance.inputType('Integer')).toBe('number');
    expect(fixture.componentInstance.inputType('String')).toBe('text');
  });
});
