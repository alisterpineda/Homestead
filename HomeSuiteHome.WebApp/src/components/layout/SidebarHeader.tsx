import { AppSwitcher } from "./AppSwitcher";
import { ThemeToggle } from "@/components/theme/ThemeToggle";
import type { ModuleDefinition } from "@/types/modules";

interface SidebarHeaderProps {
  activeModule?: ModuleDefinition;
}

export function SidebarHeader({ activeModule }: SidebarHeaderProps) {
  return (
    <div className="flex items-center gap-2 border-b border-border px-3 py-3">
      <AppSwitcher />
      {activeModule && (
        <div className="flex items-center gap-2 overflow-hidden">
          <activeModule.icon size={16} className="shrink-0 text-text-secondary" />
          <span className="truncate text-sm font-semibold text-text-primary">
            {activeModule.label}
          </span>
        </div>
      )}
      <div className="ml-auto">
        <ThemeToggle />
      </div>
    </div>
  );
}
