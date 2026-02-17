import { createFileRoute } from "@tanstack/react-router";
import { DocumentList } from "@/modules/documents/pages/DocumentList";

type DocumentsSearch = {
  page?: number;
};

export const Route = createFileRoute("/_app/documents/")({
  component: DocumentList,
  validateSearch: (search: Record<string, unknown>): DocumentsSearch => ({
    page: Number(search.page) || undefined,
  }),
});
