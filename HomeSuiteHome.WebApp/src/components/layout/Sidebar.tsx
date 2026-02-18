import { useEffect } from "react";
import { useRouterState } from "@tanstack/react-router";
import { SidebarHeader } from "./SidebarHeader";
import { SidebarNav } from "./SidebarNav";
import { useSidebar } from "@/hooks/useSidebar";
import { cn } from "@/lib/utils";
import type { ModuleDefinition } from "@/types/modules";

interface SidebarProps {
  activeModule?: ModuleDefinition;
}

export function Sidebar({ activeModule }: SidebarProps) {
  const { isOpen, close } = useSidebar();
  const pathname = useRouterState({ select: (s) => s.location.pathname });

  useEffect(() => {
    close();
  }, [pathname, close]);

  return (
    <>
      {/* Backdrop — mobile only */}
      {isOpen && (
        <div
          className="fixed inset-0 z-40 bg-black/30 md:hidden"
          onClick={close}
        />
      )}

      <aside
        className={cn(
          "fixed inset-y-0 left-0 z-50 flex h-screen w-64 flex-col border-r border-border bg-sidebar-bg transition-transform duration-200",
          isOpen ? "translate-x-0" : "-translate-x-full",
          "md:static md:z-auto md:translate-x-0 md:shrink-0 md:transition-none",
        )}
      >
        <SidebarHeader activeModule={activeModule} />
        <div className="flex-1 overflow-y-auto py-3">
          {activeModule && <SidebarNav items={activeModule.sidebar} />}
        </div>
      </aside>
    </>
  );
}
