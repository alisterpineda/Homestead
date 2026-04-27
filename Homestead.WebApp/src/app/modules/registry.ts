import type { ModuleDefinition } from '@/shared/types/modules';
import { AdminSidebarComponent } from './admin/admin-sidebar/admin-sidebar.component';
import { DocumentsSidebarComponent } from './documents/layout/documents-sidebar.component';
import { FinancesSidebarComponent } from './finances/layout/finances-sidebar.component';

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
  {
    id: 'finances',
    label: 'Finances',
    labelColor: 'emerald',
    icon: 'lucideWallet',
    basePath: '/finances',
    sidebarComponent: FinancesSidebarComponent,
  },
];

export function getModuleByPath(pathname: string): ModuleDefinition | undefined {
  return modules.find((m) => pathname.startsWith(m.basePath));
}
