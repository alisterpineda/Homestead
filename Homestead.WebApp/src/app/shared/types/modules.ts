export interface SidebarItem {
  label: string;
  path: string;
  icon?: string;
}

export interface SidebarGroup {
  label?: string;
  items: SidebarItem[];
}

export interface ModuleDefinition {
  id: string;
  label: string;
  labelColor: string;
  icon: string;
  basePath: string;
  sidebar: SidebarGroup[];
  requiredRole?: string;
}
