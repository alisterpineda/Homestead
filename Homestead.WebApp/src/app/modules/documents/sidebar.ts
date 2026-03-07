import type { SidebarGroup } from '@/shared/types/modules';

export const documentsSidebar: SidebarGroup[] = [
  {
    items: [{ label: 'Documents', path: '/documents', icon: 'lucideFileText' }],
  },
  {
    label: 'Manage',
    items: [
      { label: 'Parties', path: '/documents/parties', icon: 'lucideUsers' },
      { label: 'Tags', path: '/documents/tags', icon: 'lucideTags' },
    ],
  },
  {
    items: [{ label: 'Trash', path: '/documents/trash', icon: 'lucideTrash2' }],
  },
];
