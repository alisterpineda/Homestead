import { createContext, useContext, useState, useEffect, useCallback } from "react";

interface SidebarState {
  isOpen: boolean;
  open: () => void;
  close: () => void;
  toggle: () => void;
}

export const SidebarContext = createContext<SidebarState>({
  isOpen: false,
  open: () => {},
  close: () => {},
  toggle: () => {},
});

export function useSidebar() {
  return useContext(SidebarContext);
}

const MD_BREAKPOINT = 768;

export function useSidebarState() {
  const [isOpen, setIsOpen] = useState(false);

  const open = useCallback(() => setIsOpen(true), []);
  const close = useCallback(() => setIsOpen(false), []);
  const toggle = useCallback(() => setIsOpen((prev) => !prev), []);

  useEffect(() => {
    const mql = window.matchMedia(`(min-width: ${MD_BREAKPOINT}px)`);
    const handler = (e: MediaQueryListEvent) => {
      if (e.matches) setIsOpen(false);
    };
    mql.addEventListener("change", handler);
    return () => mql.removeEventListener("change", handler);
  }, []);

  return { isOpen, open, close, toggle };
}
