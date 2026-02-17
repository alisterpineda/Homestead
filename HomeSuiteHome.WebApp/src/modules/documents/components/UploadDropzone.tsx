import { useState, useRef, useCallback, type DragEvent } from "react";
import { Upload } from "lucide-react";
import { cn, formatBytes } from "@/lib/utils";

interface UploadDropzoneProps {
  file: File | null;
  onFileSelect: (file: File) => void;
}

export function UploadDropzone({ file, onFileSelect }: UploadDropzoneProps) {
  const [dragOver, setDragOver] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const handleDragOver = useCallback((e: DragEvent) => {
    e.preventDefault();
    setDragOver(true);
  }, []);

  const handleDragLeave = useCallback((e: DragEvent) => {
    e.preventDefault();
    setDragOver(false);
  }, []);

  const handleDrop = useCallback(
    (e: DragEvent) => {
      e.preventDefault();
      setDragOver(false);
      const dropped = e.dataTransfer.files[0];
      if (dropped) onFileSelect(dropped);
    },
    [onFileSelect],
  );

  const handleClick = () => inputRef.current?.click();

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const selected = e.target.files?.[0];
    if (selected) onFileSelect(selected);
  };

  return (
    <div
      onClick={handleClick}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
      className={cn(
        "flex cursor-pointer flex-col items-center justify-center rounded-lg border-2 border-dashed p-8 transition-colors",
        dragOver
          ? "border-accent-500 bg-accent-50"
          : file
            ? "border-accent-400 bg-accent-50"
            : "border-border hover:border-accent-400 hover:bg-surface-raised",
      )}
    >
      <input ref={inputRef} type="file" className="hidden" onChange={handleChange} />
      <Upload size={32} className={cn("mb-3", file ? "text-accent-500" : "text-text-muted")} />
      {file ? (
        <div className="text-center">
          <p className="text-sm font-medium text-text-primary">{file.name}</p>
          <p className="mt-1 text-xs text-text-secondary">{formatBytes(file.size)}</p>
        </div>
      ) : (
        <div className="text-center">
          <p className="text-sm font-medium text-text-primary">
            Drop a file here, or click to browse
          </p>
          <p className="mt-1 text-xs text-text-secondary">Any file type accepted</p>
        </div>
      )}
    </div>
  );
}
