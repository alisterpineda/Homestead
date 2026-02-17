import { createFileRoute } from "@tanstack/react-router";
import { AdminIndex } from "@/modules/admin/pages/AdminIndex";

export const Route = createFileRoute("/_app/admin/")({
  component: AdminIndex,
});
