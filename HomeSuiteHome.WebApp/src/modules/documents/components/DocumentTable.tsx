import { formatBytes } from "@/lib/utils";
import type { GetDocumentsResponse } from "@/api/generated";

interface DocumentTableProps {
  documents: GetDocumentsResponse[];
}

export function DocumentTable({ documents }: DocumentTableProps) {
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
            <tr key={doc.id} className="border-b border-border last:border-0 hover:bg-surface-raised transition-colors">
              <td className="px-4 py-3 font-medium text-text-primary">{doc.name}</td>
              <td className="px-4 py-3 text-text-secondary">{doc.fileName}</td>
              <td className="px-4 py-3 text-text-secondary">{doc.contentType}</td>
              <td className="px-4 py-3 text-text-secondary text-right">{formatBytes(doc.fileSize)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
