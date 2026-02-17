import { SidebarHeader } from "./SidebarHeader";
import { SidebarNav } from "./SidebarNav";
import type { ModuleDefinition } from "@/types/modules";

interface SidebarProps {
  activeModule?: ModuleDefinition;
}

export function Sidebar({ activeModule }: SidebarProps) {
  return (
    <aside className="flex h-screen w-64 shrink-0 flex-col border-r border-border bg-sidebar-bg">
      <SidebarHeader activeModule={activeModule} />
      <div className="flex-1 overflow-y-auto py-3">
        {activeModule && <SidebarNav items={activeModule.sidebar} />}
      </div>
    </aside>
  );
}
