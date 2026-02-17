import { Link } from "@tanstack/react-router";
import { LayoutGrid } from "lucide-react";
import { Popover } from "@/components/ui/Popover";
import { modules } from "@/modules/registry";

const colorMap: Record<string, string> = {
  slate: "bg-slate-100 text-slate-600",
  indigo: "bg-indigo-100 text-indigo-600",
  emerald: "bg-emerald-100 text-emerald-600",
  amber: "bg-amber-100 text-amber-600",
};

export function AppSwitcher() {
  return (
    <Popover
      trigger={
        <button className="rounded-md p-1.5 text-text-secondary transition-colors hover:bg-sidebar-hover hover:text-text-primary">
          <LayoutGrid size={18} />
        </button>
      }
    >
      <div className="grid w-52 grid-cols-2 gap-2">
        {modules.map((mod) => {
          const Icon = mod.icon;
          const colors = colorMap[mod.labelColor] ?? "bg-gray-100 text-gray-600";
          return (
            <Link
              key={mod.id}
              to={mod.basePath}
              className="flex flex-col items-center gap-2 rounded-lg p-3 text-center transition-colors hover:bg-surface-raised"
            >
              <div className={`rounded-lg p-2 ${colors}`}>
                <Icon size={20} />
              </div>
              <span className="text-xs font-medium text-text-primary">{mod.label}</span>
            </Link>
          );
        })}
      </div>
    </Popover>
  );
}
