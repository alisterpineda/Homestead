import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';

export type JsonPatchOperation = JsonPatchDocument[number];

export function replace(path: string, value: unknown): JsonPatchOperation {
  return { op: 'replace', path, value };
}
