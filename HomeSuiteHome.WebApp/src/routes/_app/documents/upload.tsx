import { createFileRoute } from "@tanstack/react-router";
import { DocumentUpload } from "@/modules/documents/pages/DocumentUpload";

export const Route = createFileRoute("/_app/documents/upload")({
  component: DocumentUpload,
});
