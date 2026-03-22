import type { TextExtractionEngineType } from '@/api/generated/models/text-extraction-engine-type';

export const ENGINE_TYPE_LABELS: Record<string, string> = {
  'Tesseract': 'Tesseract',
  'AzureDocumentIntelligence': 'Azure Document Intelligence',
};

export const ENGINE_TYPE_OPTIONS: { value: TextExtractionEngineType; label: string }[] = [
  { value: 'Tesseract', label: 'Tesseract' },
  { value: 'AzureDocumentIntelligence', label: 'Azure Document Intelligence' },
];
