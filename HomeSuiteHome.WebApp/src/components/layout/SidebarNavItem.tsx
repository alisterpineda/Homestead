import { Link, useRouterState } from "@tanstack/react-router";
import { cn } from "@/lib/utils";
import type { SidebarItem } from "@/types/modules";

interface SidebarNavItemProps {
  item: SidebarItem;
}

export function SidebarNavItem({ item }: SidebarNavItemProps) {
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const isActive = pathname === item.path;

  return (
    <Link
      to={item.path}
      className={cn(
        "flex items-center gap-2.5 rounded-lg px-3 py-2 text-sm font-medium transition-colors",
        isActive
          ? "bg-sidebar-active text-text-primary"
          : "text-sidebar-text hover:bg-sidebar-hover hover:text-text-primary",
      )}
    >
      {item.icon && <item.icon size={16} />}
      {item.label}
    </Link>
  );
}
