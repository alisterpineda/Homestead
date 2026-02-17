import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { customFetch } from "../client";
import type {
  PaginatedResponseOfGetDocumentsResponse,
  GetDocumentsParams,
  CreateDocumentResponse,
} from "./model";

export function getDocumentsQueryKey(params?: GetDocumentsParams) {
  return ["documents", ...(params ? [params] : [])] as const;
}

export function useGetDocuments(params?: GetDocumentsParams) {
  return useQuery({
    queryKey: getDocumentsQueryKey(params),
    queryFn: ({ signal }) =>
      customFetch<PaginatedResponseOfGetDocumentsResponse>({
        url: "/api/v1/documents",
        method: "GET",
        params: params as Record<string, string | number | boolean | undefined>,
        signal,
      }),
  });
}

export function useCreateDocument() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ name, file }: { name?: string; file: File }) => {
      const formData = new FormData();
      formData.append("file", file);
      if (name) {
        formData.append("name", name);
      }
      return customFetch<CreateDocumentResponse>({
        url: "/api/v1/documents",
        method: "POST",
        data: formData,
      });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["documents"] });
    },
  });
}
