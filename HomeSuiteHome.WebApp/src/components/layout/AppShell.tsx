import { useRouterState } from "@tanstack/react-router";
import { Menu } from "lucide-react";
import { Sidebar } from "./Sidebar";
import { getModuleByPath } from "@/modules/registry";
import { SidebarContext, useSidebarState } from "@/hooks/useSidebar";
import type { ReactNode } from "react";

interface AppShellProps {
  children: ReactNode;
}

export function AppShell({ children }: AppShellProps) {
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const activeModule = getModuleByPath(pathname);
  const sidebarState = useSidebarState();

  return (
    <SidebarContext.Provider value={sidebarState}>
      <div className="flex h-screen overflow-hidden">
        <Sidebar activeModule={activeModule} />
        <div className="flex flex-1 flex-col overflow-hidden">
          <div className="sticky top-0 z-40 flex items-center gap-3 border-b border-border bg-surface px-4 py-3 md:hidden">
            <button
              onClick={sidebarState.toggle}
              className="rounded-lg p-1.5 text-text-secondary hover:bg-surface-raised hover:text-text-primary"
            >
              <Menu size={20} />
            </button>
            {activeModule && (
              <div className="flex items-center gap-2">
                <activeModule.icon size={16} className="text-text-secondary" />
                <span className="text-sm font-semibold text-text-primary">
                  {activeModule.label}
                </span>
              </div>
            )}
          </div>
          <main className="flex-1 overflow-y-auto p-4 md:p-8">{children}</main>
        </div>
      </div>
    </SidebarContext.Provider>
  );
}
