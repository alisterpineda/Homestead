import { X } from "lucide-react";
import { Badge } from "@/components/ui/Badge";

interface TagBadgeProps {
  path: string;
  onRemove?: () => void;
}

export function TagBadge({ path, onRemove }: TagBadgeProps) {
  return (
    <Badge className="gap-1">
      {path}
      {onRemove && (
        <button
          type="button"
          onClick={(e) => {
            e.stopPropagation();
            onRemove();
          }}
          className="ml-0.5 rounded hover:bg-surface-overlay"
        >
          <X size={12} />
        </button>
      )}
    </Badge>
  );
}
