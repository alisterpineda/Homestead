import { useState } from "react";
import { useNavigate } from "@tanstack/react-router";
import { useQueryClient } from "@tanstack/react-query";
import { useCreateDocument, getGetDocumentsQueryKey } from "@/api/generated";
import { PageHeader } from "@/components/ui/PageHeader";
import { Card } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";
import { Button } from "@/components/ui/Button";
import { UploadDropzone } from "../components/UploadDropzone";

export function DocumentUpload() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const mutation = useCreateDocument();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!file) return;

    mutation.mutate(
      { data: { name: name || undefined, file } },
      {
        onSuccess: () => {
          queryClient.invalidateQueries({ queryKey: getGetDocumentsQueryKey() });
          navigate({ to: "/documents" });
        },
      },
    );
  };

  return (
    <div>
      <PageHeader title="Upload Document" description="Upload a new document to the system" />
      <Card className="max-w-xl">
        <form onSubmit={handleSubmit} className="flex flex-col gap-5">
          <Input
            id="name"
            label="Document Name (optional)"
            placeholder="Enter a name for this document"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
          <div className="flex flex-col gap-1.5">
            <label className="text-sm font-medium text-text-secondary">File</label>
            <UploadDropzone file={file} onFileSelect={setFile} />
          </div>
          {mutation.isError && (
            <p className="text-sm text-red-500">
              Upload failed. Please try again.
            </p>
          )}
          <div className="flex gap-3">
            <Button type="submit" disabled={!file || mutation.isPending}>
              {mutation.isPending ? "Uploading..." : "Upload"}
            </Button>
            <Button
              type="button"
              variant="secondary"
              onClick={() => navigate({ to: "/documents" })}
            >
              Cancel
            </Button>
          </div>
        </form>
      </Card>
    </div>
  );
}
