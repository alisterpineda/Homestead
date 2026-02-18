import { X, Download, Trash2, FileText } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import {
  useGetDocument,
  useDeleteDocument,
  getGetDocumentsQueryKey,
} from "@/api/generated";
import { Sheet } from "@/components/ui/Sheet";
import { Tabs } from "@/components/ui/Tabs";
import { Button } from "@/components/ui/Button";
import { Spinner } from "@/components/ui/Spinner";
import { formatBytes } from "@/lib/utils";

interface DocumentDetailPanelProps {
  documentId: string | null;
  onClose: () => void;
}

export function DocumentDetailPanel({
  documentId,
  onClose,
}: DocumentDetailPanelProps) {
  const queryClient = useQueryClient();
  const { data: document, isLoading } = useGetDocument(documentId ?? "", {
    query: { enabled: !!documentId },
  });
  const deleteMutation = useDeleteDocument({
    mutation: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: getGetDocumentsQueryKey() });
        onClose();
      },
    },
  });

  const isPdf = document?.contentType === "application/pdf";
  const fileUrl = documentId
    ? `/api/v1/documents/${documentId}/file`
    : undefined;

  return (
    <Sheet open={!!documentId} onClose={onClose}>
      {isLoading && (
        <div className="flex h-full items-center justify-center">
          <Spinner size="lg" />
        </div>
      )}

      {document && (
        <div className="flex h-full flex-col">
          {/* Header */}
          <div className="flex items-center justify-between border-b border-border px-4 py-3">
            <h2 className="truncate text-lg font-semibold text-text-primary">
              {document.fileName}
            </h2>
            <button
              onClick={onClose}
              className="rounded-lg p-1 text-text-secondary hover:bg-surface-raised hover:text-text-primary"
            >
              <X size={20} />
            </button>
          </div>

          {/* Preview */}
          <div className="border-b border-border bg-surface-raised">
            {isPdf && fileUrl ? (
              <iframe
                src={fileUrl}
                className="h-80 w-full"
                title={`Preview of ${document.fileName}`}
              />
            ) : (
              <div className="flex h-48 flex-col items-center justify-center gap-2 text-text-secondary">
                <FileText size={48} strokeWidth={1} />
                <span className="text-sm">Preview not available</span>
              </div>
            )}
          </div>

          {/* Actions */}
          <div className="flex gap-2 border-b border-border px-4 py-3">
            <a
              href={`${fileUrl}?download=true`}
              download
              className="inline-flex items-center justify-center gap-2 rounded-lg border border-border bg-surface-raised px-4 py-2 text-sm font-medium text-text-primary transition-colors hover:bg-surface-overlay"
            >
              <Download size={16} />
              Download
            </a>
            <Button
              variant="ghost"
              onClick={() => {
                if (documentId) {
                  deleteMutation.mutate({ id: documentId });
                }
              }}
              disabled={deleteMutation.isPending}
              className="text-red-500 hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-950"
            >
              <Trash2 size={16} />
              {deleteMutation.isPending ? "Deleting..." : "Delete"}
            </Button>
          </div>

          {/* Tabs */}
          <div className="flex-1 px-4 py-3">
            <Tabs
              tabs={[
                {
                  id: "info",
                  label: "Info",
                  content: (
                    <div className="space-y-3">
                      <InfoRow
                        label="Added"
                        value={new Date(
                          document.createdAt,
                        ).toLocaleDateString(undefined, {
                          year: "numeric",
                          month: "long",
                          day: "numeric",
                        })}
                      />
                      <InfoRow
                        label="Size"
                        value={formatBytes(Number(document.fileSize))}
                      />
                      <InfoRow label="Type" value={document.contentType} />
                    </div>
                  ),
                },
              ]}
            />
          </div>
        </div>
      )}
    </Sheet>
  );
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between text-sm">
      <span className="text-text-secondary">{label}</span>
      <span className="text-text-primary">{value}</span>
    </div>
  );
}
