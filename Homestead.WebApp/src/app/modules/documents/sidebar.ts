import type { SidebarGroup } from '@/shared/types/modules';

export const documentsSidebar: SidebarGroup[] = [
  {
    items: [{ label: 'Documents', path: '/documents', icon: 'lucideFileText' }],
  },
  {
    label: 'Manage',
    items: [{ label: 'Parties', path: '/documents/parties', icon: 'lucideUsers' }],
  },
];
