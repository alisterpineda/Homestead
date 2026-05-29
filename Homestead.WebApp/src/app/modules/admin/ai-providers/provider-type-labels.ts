import type { AiProviderType } from '@/api/generated/models/ai-provider-type';

export const PROVIDER_TYPE_LABELS: Record<string, string> = {
  'AzureAIFoundry': 'Azure AI Foundry',
  'OpenAI': 'OpenAI',
  'Anthropic': 'Anthropic',
};

// Anthropic is a forward-compat enum value only — it has a label for rendering existing rows but
// is not yet a selectable create option.
export const PROVIDER_TYPE_OPTIONS: { value: AiProviderType; label: string }[] = [
  { value: 'AzureAIFoundry', label: 'Azure AI Foundry' },
  { value: 'OpenAI', label: 'OpenAI' },
];
