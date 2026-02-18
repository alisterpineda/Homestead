import { formatBytes, cn } from "@/lib/utils";
import type { GetDocumentsResponse } from "@/api/generated";

interface DocumentTableProps {
  documents: GetDocumentsResponse[];
  selectedDocumentId?: string | null;
  onDocumentSelect?: (id: string) => void;
}

export function DocumentTable({ documents, selectedDocumentId, onDocumentSelect }: DocumentTableProps) {
  return (
    <div className="overflow-hidden rounded-lg border border-border">
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
  );
}
