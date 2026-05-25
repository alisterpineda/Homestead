import type { AiProviderType } from '@/api/generated/models/ai-provider-type';

export const PROVIDER_TYPE_LABELS: Record<string, string> = {
  'AzureAIFoundry': 'Azure AI Foundry',
};

export const PROVIDER_TYPE_OPTIONS: { value: AiProviderType; label: string }[] = [
  { value: 'AzureAIFoundry', label: 'Azure AI Foundry' },
];
