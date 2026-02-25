export interface SidebarItem {
  label: string;
  path: string;
  icon?: string;
}

export interface ModuleDefinition {
  id: string;
  label: string;
  labelColor: string;
  icon: string;
  basePath: string;
  sidebar: SidebarItem[];
}
