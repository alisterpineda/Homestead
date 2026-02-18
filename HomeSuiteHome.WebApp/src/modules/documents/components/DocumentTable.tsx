import { formatBytes, cn } from "@/lib/utils";
import type { GetDocumentsResponse } from "@/api/generated";

interface DocumentTableProps {
  documents: GetDocumentsResponse[];
  selectedDocumentId?: string | null;
  onDocumentSelect?: (id: string) => void;
}

export function DocumentTable({ documents, selectedDocumentId, onDocumentSelect }: DocumentTableProps) {
  return (
    <>
      {/* Mobile card layout */}
      <div className="flex flex-col gap-3 md:hidden">
        {documents.map((doc) => (
          <div
            key={doc.id}
            onClick={() => onDocumentSelect?.(doc.id)}
            className={cn(
              "cursor-pointer rounded-lg border border-border p-4 transition-colors",
              selectedDocumentId === doc.id
                ? "bg-accent-50 dark:bg-accent-950"
                : "hover:bg-surface-raised",
            )}
          >
            <p className="font-medium text-text-primary">{doc.name}</p>
            <p className="mt-1 truncate text-sm text-text-secondary">{doc.fileName}</p>
            <div className="mt-2 flex items-center gap-3 text-xs text-text-muted">
              <span>{doc.contentType}</span>
              <span>{formatBytes(Number(doc.fileSize))}</span>
            </div>
          </div>
        ))}
      </div>

      {/* Desktop table layout */}
      <div className="hidden overflow-hidden rounded-lg border border-border md:block">
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-border bg-surface-raised">
              <th className="px-4 py-3 font-medium text-text-secondary">Name</th>
              <th className="px-4 py-3 font-medium text-text-secondary">File Name</th>
              <th className="px-4 py-3 font-medium text-text-secondary">Content Type</th>
              <th className="px-4 py-3 font-medium text-text-secondary text-right">File Size</th>
            </tr>
          </thead>
          <tbody>
            {documents.map((doc) => (
              <tr
                key={doc.id}
                onClick={() => onDocumentSelect?.(doc.id)}
                className={cn(
                  "border-b border-border last:border-0 cursor-pointer transition-colors",
                  selectedDocumentId === doc.id
                    ? "bg-accent-50 dark:bg-accent-950"
                    : "hover:bg-surface-raised",
                )}
              >
                <td className="px-4 py-3 font-medium text-text-primary">{doc.name}</td>
                <td className="px-4 py-3 text-text-secondary">{doc.fileName}</td>
                <td className="px-4 py-3 text-text-secondary">{doc.contentType}</td>
                <td className="px-4 py-3 text-text-secondary text-right">{formatBytes(Number(doc.fileSize))}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
