import { Settings, FileText } from "lucide-react";
import type { ModuleDefinition } from "@/types/modules";
import { adminSidebar } from "./admin/sidebar";
import { documentsSidebar } from "./documents/sidebar";

export const modules: ModuleDefinition[] = [
  {
    id: "admin",
    label: "Admin",
    labelColor: "slate",
    icon: Settings,
    basePath: "/admin",
    sidebar: adminSidebar,
  },
  {
    id: "documents",
    label: "Documents",
    labelColor: "indigo",
    icon: FileText,
    basePath: "/documents",
    sidebar: documentsSidebar,
  },
];

export function getModuleByPath(pathname: string): ModuleDefinition | undefined {
  return modules.find((m) => pathname.startsWith(m.basePath));
}
