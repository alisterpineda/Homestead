import type { HttpValidationProblemDetails } from '@/api/generated/models/http-validation-problem-details';

export interface ApiProblemDetails extends HttpValidationProblemDetails {
  errorCode?: string | null;
}
