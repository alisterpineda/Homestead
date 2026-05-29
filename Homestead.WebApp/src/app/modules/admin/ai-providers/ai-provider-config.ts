import type { AiProviderType } from '@/api/generated/models/ai-provider-type';

export interface AiProviderConfigFields {
  endpoint: string;
  apiKey: string;
  model: string;
}

export interface AiProviderTestResult {
  success: boolean;
  message: string;
}

// All provider types share the same JSON config keys (endpoint/apiKey/model). Trim so trailing
// whitespace can't be persisted into the model/endpoint and break the live provider call later.
export function buildAiProviderConfiguration(fields: AiProviderConfigFields): string {
  return JSON.stringify({
    endpoint: fields.endpoint.trim(),
    apiKey: fields.apiKey.trim(),
    model: fields.model.trim(),
  });
}

// Required fields per provider type (excluding the display name) — gates the Test/Submit actions.
// Single source of truth shared by the create dialog and edit sheet so the two can't drift.
export function aiProviderConfigComplete(type: AiProviderType, fields: AiProviderConfigFields): boolean {
  switch (type) {
    case 'AzureAIFoundry':
      return fields.endpoint.trim().length > 0
        && fields.apiKey.trim().length > 0
        && fields.model.trim().length > 0;
    case 'OpenAI':
      return fields.apiKey.trim().length > 0
        && fields.model.trim().length > 0;
    default:
      return false;
  }
}
