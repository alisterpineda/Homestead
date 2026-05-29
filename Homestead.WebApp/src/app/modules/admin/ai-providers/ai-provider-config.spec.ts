import { aiProviderConfigComplete, buildAiProviderConfiguration } from './ai-provider-config';

describe('buildAiProviderConfiguration', () => {
  it('trims all fields and uses the shared JSON keys', () => {
    const json = buildAiProviderConfiguration({ endpoint: '  https://x/v1  ', apiKey: ' sk-key ', model: ' gpt-4o ' });
    expect(JSON.parse(json)).toEqual({ endpoint: 'https://x/v1', apiKey: 'sk-key', model: 'gpt-4o' });
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
