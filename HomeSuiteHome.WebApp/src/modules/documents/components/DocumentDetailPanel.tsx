import { useState, useEffect } from "react";
import { X, Download, Trash2, FileText, Pencil, Check } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import {
  useGetDocument,
  useDeleteDocument,
  useUpdateDocument,
  getGetDocumentsQueryKey,
  getGetDocumentQueryKey,
} from "@/api/generated";
import { Sheet } from "@/components/ui/Sheet";
import { Tabs } from "@/components/ui/Tabs";
import { Button } from "@/components/ui/Button";
import { Spinner } from "@/components/ui/Spinner";
import { formatBytes } from "@/lib/utils";
import { TagBadge } from "./TagBadge";
import { TagInput } from "./TagInput";

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
  const updateMutation = useUpdateDocument({
    mutation: {
      onSuccess: () => {
        queryClient.invalidateQueries({
          queryKey: getGetDocumentQueryKey(documentId ?? ""),
        });
        queryClient.invalidateQueries({ queryKey: getGetDocumentsQueryKey() });
        setEditing(false);
      },
    },
  });

  const [editing, setEditing] = useState(false);
  const [editName, setEditName] = useState("");
  const [editTags, setEditTags] = useState<string[]>([]);

  useEffect(() => {
    if (document) {
      setEditName(document.name);
      setEditTags(document.tags?.map((t) => t.path) ?? []);
    }
  }, [document]);

  useEffect(() => {
    setEditing(false);
  }, [documentId]);

  const handleSave = () => {
    if (!documentId) return;
    updateMutation.mutate({
      id: documentId,
      data: { name: editName, tagPaths: editTags },
    });
  };

  const handleCancelEdit = () => {
    setEditing(false);
    if (document) {
      setEditName(document.name);
      setEditTags(document.tags?.map((t) => t.path) ?? []);
    }
  };

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
          <div className="flex-1 overflow-y-auto px-4 py-3">
            <Tabs
              tabs={[
                {
                  id: "info",
                  label: "Info",
                  content: (
                    <div className="space-y-3">
                      {editing ? (
                        <>
                          <div className="flex flex-col gap-1.5">
                            <label className="text-sm font-medium text-text-secondary">
                              Name
                            </label>
                            <input
                              value={editName}
                              onChange={(e) => setEditName(e.target.value)}
                              className="rounded-lg border border-border bg-surface px-3 py-2 text-sm text-text-primary transition-colors focus:border-accent-500 focus:outline-none focus:ring-2 focus:ring-accent-500/20"
                            />
                          </div>
                          <div className="flex flex-col gap-1.5">
                            <label className="text-sm font-medium text-text-secondary">
                              Tags
                            </label>
                            <TagInput value={editTags} onChange={setEditTags} />
                          </div>
                          <div className="flex gap-2 pt-1">
                            <Button
                              variant="primary"
                              onClick={handleSave}
                              disabled={updateMutation.isPending}
                            >
                              <Check size={14} />
                              {updateMutation.isPending ? "Saving..." : "Save"}
                            </Button>
                            <Button
                              variant="ghost"
                              onClick={handleCancelEdit}
                              disabled={updateMutation.isPending}
                            >
                              Cancel
                            </Button>
                          </div>
                        </>
                      ) : (
                        <>
                          <div className="flex items-start justify-between">
                            <div className="space-y-3 flex-1">
                              <InfoRow label="Name" value={document.name} />
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
                              <InfoRow
                                label="Type"
                                value={document.contentType}
                              />
                            </div>
                            <button
                              onClick={() => setEditing(true)}
                              className="rounded-lg p-1.5 text-text-secondary hover:bg-surface-raised hover:text-text-primary"
                            >
                              <Pencil size={14} />
                            </button>
                          </div>
                          <div className="pt-1">
                            <span className="text-sm text-text-secondary">
                              Tags
                            </span>
                            <div className="mt-1.5 flex flex-wrap gap-1.5">
                              {document.tags && document.tags.length > 0 ? (
                                document.tags.map((tag) => (
                                  <TagBadge key={tag.id} path={tag.path} />
                                ))
                              ) : (
                                <span className="text-sm text-text-muted">
                                  No tags
                                </span>
                              )}
                            </div>
                          </div>
                        </>
                      )}
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
