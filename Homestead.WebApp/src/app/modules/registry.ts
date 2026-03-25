import type { ModuleDefinition } from '@/shared/types/modules';
import { AdminSidebarComponent } from './admin/admin-sidebar/admin-sidebar.component';
import { DocumentsSidebarComponent } from './documents/layout/documents-sidebar.component';

export const modules: ModuleDefinition[] = [
  {
    id: 'admin',
    label: 'Admin',
    labelColor: 'slate',
    icon: 'lucideSettings',
    basePath: '/admin',
    sidebarComponent: AdminSidebarComponent,
    requiredRole: 'Admin',
  },
  {
    id: 'documents',
    label: 'Documents',
    labelColor: 'indigo',
    icon: 'lucideFileText',
    basePath: '/documents',
    sidebarComponent: DocumentsSidebarComponent,
  },
];

export function getModuleByPath(pathname: string): ModuleDefinition | undefined {
  return modules.find((m) => pathname.startsWith(m.basePath));
}
