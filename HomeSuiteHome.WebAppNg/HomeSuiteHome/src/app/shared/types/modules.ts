import type { LucideIconData } from 'lucide-angular';

export interface SidebarItem {
  label: string;
  path: string;
  icon?: LucideIconData;
}

export interface ModuleDefinition {
  id: string;
  label: string;
  labelColor: string;
  icon: LucideIconData;
  basePath: string;
  sidebar: SidebarItem[];
}
