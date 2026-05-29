import { PROVIDER_TYPE_LABELS, PROVIDER_TYPE_OPTIONS } from './provider-type-labels';

describe('provider-type-labels', () => {
  it('has labels for every provider type, including Anthropic', () => {
    expect(PROVIDER_TYPE_LABELS['AzureAIFoundry']).toBe('Azure AI Foundry');
    expect(PROVIDER_TYPE_LABELS['OpenAI']).toBe('OpenAI');
    expect(PROVIDER_TYPE_LABELS['Anthropic']).toBe('Anthropic');
  });

  it('offers OpenAI and Azure as create options', () => {
    const values = PROVIDER_TYPE_OPTIONS.map(o => o.value);
    expect(values).toContain('OpenAI');
    expect(values).toContain('AzureAIFoundry');
  });

  it('does not offer Anthropic as a create option (forward-compat enum value only)', () => {
    const values = PROVIDER_TYPE_OPTIONS.map(o => o.value);
    expect(values).not.toContain('Anthropic');
  });
});
