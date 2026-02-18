import { useState } from "react";
import { Link, useNavigate, useSearch } from "@tanstack/react-router";
import { FileText, Plus } from "lucide-react";
import { useGetDocuments } from "@/api/generated";
import { PageHeader } from "@/components/ui/PageHeader";
import { Button } from "@/components/ui/Button";
import { Spinner } from "@/components/ui/Spinner";
import { EmptyState } from "@/components/ui/EmptyState";
import { DocumentTable } from "../components/DocumentTable";
import { DocumentDetailPanel } from "../components/DocumentDetailPanel";
import { Pagination } from "../components/Pagination";

export function DocumentList() {
  const search = useSearch({ from: "/_app/documents/" });
  const navigate = useNavigate();
  const page = (search as { page?: number }).page ?? 1;
  const [selectedDocumentId, setSelectedDocumentId] = useState<string | null>(null);

  const { data, isLoading, isError } = useGetDocuments({ Page: page, PageSize: 10 });

  const handlePageChange = (newPage: number) => {
    navigate({ to: "/documents", search: { page: newPage } });
  };

  return (
    <div>
      <PageHeader
        title="Documents"
        description="Manage your uploaded documents"
        action={
          <Link to="/documents/upload">
            <Button>
              <Plus size={16} />
              Upload
            </Button>
          </Link>
        }
      />

      {isLoading && (
        <div className="flex justify-center py-16">
          <Spinner size="lg" />
        </div>
      )}

      {isError && (
        <EmptyState
          icon={FileText}
          title="Failed to load documents"
          description="There was an error loading the document list. Please try again."
        />
      )}

      {data && data.items.length === 0 && (
        <EmptyState
          icon={FileText}
          title="No documents yet"
          description="Upload your first document to get started."
          action={
            <Link to="/documents/upload">
              <Button>
                <Plus size={16} />
                Upload Document
              </Button>
            </Link>
          }
        />
      )}

      {data && data.items.length > 0 && (
        <>
          <DocumentTable
            documents={data.items}
            selectedDocumentId={selectedDocumentId}
            onDocumentSelect={setSelectedDocumentId}
          />
          <Pagination
            page={Number(data.page)}
            totalPages={Number(data.totalPages)}
            onPageChange={handlePageChange}
          />
        </>
      )}

      <DocumentDetailPanel
        documentId={selectedDocumentId}
        onClose={() => setSelectedDocumentId(null)}
      />
    </div>
  );
}
