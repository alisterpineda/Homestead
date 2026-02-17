import { Settings } from "lucide-react";
import { PageHeader } from "@/components/ui/PageHeader";
import { EmptyState } from "@/components/ui/EmptyState";

export function AdminIndex() {
  return (
    <div>
      <PageHeader title="Admin" description="System administration and settings" />
      <EmptyState
        icon={Settings}
        title="Admin features coming soon"
        description="Configuration and system management tools will be available here."
      />
    </div>
  );
}
