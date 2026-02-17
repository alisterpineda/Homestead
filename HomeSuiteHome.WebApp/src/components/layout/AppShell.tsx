import { useRouterState } from "@tanstack/react-router";
import { Sidebar } from "./Sidebar";
import { getModuleByPath } from "@/modules/registry";
import type { ReactNode } from "react";

interface AppShellProps {
  children: ReactNode;
}

export function AppShell({ children }: AppShellProps) {
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const activeModule = getModuleByPath(pathname);

  return (
    <div className="flex h-screen overflow-hidden">
      <Sidebar activeModule={activeModule} />
      <main className="flex-1 overflow-y-auto p-8">{children}</main>
    </div>
  );
}
