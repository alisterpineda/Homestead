import type { SidebarGroup } from '@/shared/types/modules';

export const adminSidebar: SidebarGroup[] = [
  {
    items: [{ label: 'Overview', path: '/admin', icon: 'lucideLayoutDashboard' }],
  },
  {
    label: 'Manage',
    items: [
      { label: 'Users', path: '/admin/users', icon: 'lucideUsers' },
      { label: 'Groups', path: '/admin/groups', icon: 'lucideUsers2' },
      { label: 'Jobs', path: '/admin/jobs', icon: 'lucideActivity' },
    ],
  },
];
