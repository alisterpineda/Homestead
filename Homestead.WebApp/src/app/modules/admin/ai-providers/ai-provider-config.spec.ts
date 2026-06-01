import { aiProviderConfigComplete, buildAiProviderConfiguration, parseMaxInputChars } from './ai-provider-config';

describe('buildAiProviderConfiguration', () => {
  it('trims all fields and uses the shared JSON keys', () => {
    const json = buildAiProviderConfiguration({ endpoint: '  https://x/v1  ', apiKey: ' sk-key ', model: ' gpt-4o ' });
    expect(JSON.parse(json)).toEqual({ endpoint: 'https://x/v1', apiKey: 'sk-key', model: 'gpt-4o' });
  });

  it('omits maxInputChars when blank', () => {
    const json = buildAiProviderConfiguration({ endpoint: '', apiKey: 'k', model: 'm', maxInputChars: '' });
    expect('maxInputChars' in JSON.parse(json)).toBe(false);
  });

  it('includes maxInputChars as a number when a valid value is set', () => {
    const json = buildAiProviderConfiguration({ endpoint: '', apiKey: 'k', model: 'm', maxInputChars: ' 40000 ' });
    expect(JSON.parse(json).maxInputChars).toBe(40000);
  });

  it('omits maxInputChars when non-numeric or non-positive', () => {
    expect('maxInputChars' in JSON.parse(buildAiProviderConfiguration({ endpoint: '', apiKey: 'k', model: 'm', maxInputChars: 'abc' }))).toBe(false);
    expect('maxInputChars' in JSON.parse(buildAiProviderConfiguration({ endpoint: '', apiKey: 'k', model: 'm', maxInputChars: '0' }))).toBe(false);
  });
});

describe('parseMaxInputChars', () => {
  it('returns null for blank/undefined/whitespace', () => {
    expect(parseMaxInputChars('')).toBeNull();
    expect(parseMaxInputChars(undefined)).toBeNull();
    expect(parseMaxInputChars('   ')).toBeNull();
  });

  it('returns the integer for a valid positive value', () => {
    expect(parseMaxInputChars('40000')).toBe(40000);
    expect(parseMaxInputChars(' 12000 ')).toBe(12000);
  });

  it('returns null for non-integer, zero, or negative values', () => {
    expect(parseMaxInputChars('12.5')).toBeNull();
    expect(parseMaxInputChars('0')).toBeNull();
    expect(parseMaxInputChars('-5')).toBeNull();
    expect(parseMaxInputChars('abc')).toBeNull();
  });
});

describe('aiProviderConfigComplete', () => {
  it('requires endpoint, apiKey and model for AzureAIFoundry', () => {
    expect(aiProviderConfigComplete('AzureAIFoundry', { endpoint: 'https://x', apiKey: 'k', model: 'm' })).toBe(true);
    expect(aiProviderConfigComplete('AzureAIFoundry', { endpoint: '', apiKey: 'k', model: 'm' })).toBe(false);
    expect(aiProviderConfigComplete('AzureAIFoundry', { endpoint: 'https://x', apiKey: 'k', model: '' })).toBe(false);
  });

  it('requires only apiKey and model for OpenAI (endpoint optional)', () => {
    expect(aiProviderConfigComplete('OpenAI', { endpoint: '', apiKey: 'k', model: 'm' })).toBe(true);
    expect(aiProviderConfigComplete('OpenAI', { endpoint: '', apiKey: '', model: 'm' })).toBe(false);
    expect(aiProviderConfigComplete('OpenAI', { endpoint: '', apiKey: 'k', model: '' })).toBe(false);
  });

  it('treats whitespace-only values as missing', () => {
    expect(aiProviderConfigComplete('OpenAI', { endpoint: '', apiKey: '   ', model: 'm' })).toBe(false);
  });

  it('returns false for unsupported provider types (e.g. Anthropic)', () => {
    expect(aiProviderConfigComplete('Anthropic', { endpoint: 'https://x', apiKey: 'k', model: 'm' })).toBe(false);
  });
});
