import { Sun, Moon, Monitor } from "lucide-react";
import { useTheme, type ThemeMode } from "./useTheme";

const modes: { value: ThemeMode; icon: typeof Sun; label: string }[] = [
  { value: "light", icon: Sun, label: "Light" },
  { value: "dark", icon: Moon, label: "Dark" },
  { value: "system", icon: Monitor, label: "System" },
];

export function ThemeToggle() {
  const { mode, setMode } = useTheme();

  const cycleTheme = () => {
    const currentIndex = modes.findIndex((m) => m.value === mode);
    const next = modes[(currentIndex + 1) % modes.length]!;
    setMode(next.value);
  };

  const current = modes.find((m) => m.value === mode) ?? modes[0]!;
  const Icon = current.icon;

  return (
    <button
      onClick={cycleTheme}
      className="rounded-md p-1.5 text-text-secondary transition-colors hover:bg-sidebar-hover hover:text-text-primary"
      title={`Theme: ${current.label}`}
    >
      <Icon size={18} />
    </button>
  );
}
