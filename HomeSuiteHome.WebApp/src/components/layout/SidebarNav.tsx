import type { SidebarItem } from "@/types/modules";
import { SidebarNavItem } from "./SidebarNavItem";

interface SidebarNavProps {
  items: SidebarItem[];
}

export function SidebarNav({ items }: SidebarNavProps) {
  return (
    <nav className="flex flex-col gap-1 px-3">
      {items.map((item) => (
        <SidebarNavItem key={item.path} item={item} />
      ))}
    </nav>
  );
}
