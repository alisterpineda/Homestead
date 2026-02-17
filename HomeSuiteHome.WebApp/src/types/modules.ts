import type { LucideIcon } from "lucide-react";

export interface SidebarItem {
  label: string;
  path: string;
  icon?: LucideIcon;
}

export interface ModuleDefinition {
  id: string;
  label: string;
  labelColor: string;
  icon: LucideIcon;
  basePath: string;
  sidebar: SidebarItem[];
}
