import type { ModuleDefinition } from '@/shared/types/modules';
import { adminSidebar } from './admin/sidebar';
import { documentsSidebar } from './documents/sidebar';

export const modules: ModuleDefinition[] = [
  {
    id: 'admin',
    label: 'Admin',
    labelColor: 'slate',
    icon: 'lucideSettings',
    basePath: '/admin',
    sidebar: adminSidebar,
    requiredRole: 'Admin',
  },
  {
    id: 'documents',
    label: 'Documents',
    labelColor: 'indigo',
    icon: 'lucideFileText',
    basePath: '/documents',
    sidebar: documentsSidebar,
  },
];

export function getModuleByPath(pathname: string): ModuleDefinition | undefined {
  return modules.find((m) => pathname.startsWith(m.basePath));
}
